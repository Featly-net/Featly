using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using ChangeNotification = Featly.Storage.ChangeNotification;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// SDK-facing endpoints: a polled snapshot with ETag support and an SSE
/// stream for low-latency change notifications.
/// </summary>
internal static class SdkEndpoints
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static RouteGroupBuilder MapSdkEndpoints(this RouteGroupBuilder group)
    {
        var sdk = group.MapGroup("/sdk").RequireAuthorization(FeatlyAuthenticationDefaults.SdkPolicy);

        sdk.MapGet("/config", GetConfigAsync).WithName("Featly.Sdk.Config");
        sdk.MapGet("/stream", StreamAsync).WithName("Featly.Sdk.Stream");

        return group;
    }

    private static async Task GetConfigAsync(HttpContext context, StorageFacade store, Telemetry.SdkActivityTracker activity, string? env, CancellationToken ct)
    {
        var bound = Authentication.SdkEnvironmentScope.BoundEnvironmentId(context.User);
        var environment = await Authentication.SdkEnvironmentScope.ResolveAsync(store, env, bound, ct).ConfigureAwait(false);
        if (environment is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new { error = $"Environment '{env}' not found." }, ct).ConfigureAwait(false);
            return;
        }
        if (!Authentication.SdkEnvironmentScope.Allows(bound, environment.Id))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = "This API key is scoped to a different environment." }, ct).ConfigureAwait(false);
            return;
        }

        // This IS the network round-trip the SDK already makes to sync its
        // cache (poll or ETag revalidation) — recording it observes an
        // existing call rather than adding one, so the "no network call on
        // the hot path" evaluation guarantee is untouched.
        activity.RecordConfigSync(environment.Id);

        // Compose the ETag from the most recent flag, segment, AND config update
        // so edits in any of the three buckets invalidate cached snapshots on
        // the SDK side.
        var flagsMostRecent = await store.Flags.GetMostRecentUpdateAsync(environment.Id, ct).ConfigureAwait(false);
        var segmentsMostRecent = await store.Segments.GetMostRecentUpdateAsync(environment.Id, ct).ConfigureAwait(false);
        var configsMostRecent = await store.Configs.GetMostRecentUpdateAsync(environment.Id, ct).ConfigureAwait(false);
        var experimentsMostRecent = await store.Experiments.GetMostRecentUpdateAsync(environment.Id, ct).ConfigureAwait(false);
        var mostRecent = Latest(Latest(Latest(flagsMostRecent, segmentsMostRecent), configsMostRecent), experimentsMostRecent);
        var etag = ComputeEtag(environment.Id, mostRecent);
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "no-cache";

        if (context.Request.Headers.IfNoneMatch.Count > 0 &&
            context.Request.Headers.IfNoneMatch.Any(value => value == etag))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        var flags = await store.Flags.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var segments = await store.Segments.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var configs = await store.Configs.ListAsync(environment.Id, ct).ConfigureAwait(false);
        // Only active experiments ship in the snapshot — the SDK emits an
        // exposure whenever it evaluates a flag that an active experiment covers.
        var experiments = await store.Experiments.ListActiveAsync(environment.Id, ct).ConfigureAwait(false);
        var snapshot = new ConfigSnapshot(
            EnvironmentId: environment.Id,
            EnvironmentKey: environment.Key,
            At: DateTimeOffset.UtcNow,
            Flags: flags,
            Segments: segments,
            Configs: configs,
            Experiments: experiments);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await JsonSerializer.SerializeAsync(context.Response.Body, snapshot, s_jsonOptions, ct).ConfigureAwait(false);
    }

    // ConfigSnapshot record moved to Featly.Abstractions so the SDK shares the same shape.

    private static async Task StreamAsync(HttpContext context, StorageFacade store, Telemetry.SdkActivityTracker activity, string? env, CancellationToken ct)
    {
        var bound = Authentication.SdkEnvironmentScope.BoundEnvironmentId(context.User);
        var environment = await Authentication.SdkEnvironmentScope.ResolveAsync(store, env, bound, ct).ConfigureAwait(false);
        if (environment is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!Authentication.SdkEnvironmentScope.Allows(bound, environment.Id))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Tracks the connection for the life of the SSE stream; decremented
        // when the client disconnects (ct canceled) via the finally block below.
        using var lease = activity.RecordStreamConnected(environment.Id);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-transform";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        // Disable response buffering so events flush to the wire immediately.
        var bufferingFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        var channel = Channel.CreateUnbounded<ChangeNotification>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        using var subscription = store.Changes.Subscribe(async (notification, innerCt) =>
        {
            if (notification.EnvironmentId is null || notification.EnvironmentId == environment.Id)
            {
                await channel.Writer.WriteAsync(notification, innerCt).ConfigureAwait(false);
            }
        });

        // Emit one hello frame so the client can confirm the connection is live.
        await WriteEventAsync(context.Response, "hello",
            new { environmentId = environment.Id, environmentKey = environment.Key, at = DateTimeOffset.UtcNow },
            ct).ConfigureAwait(false);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ChangeNotification? notification = null;
                try
                {
                    notification = await channel.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (notification is not null)
                {
                    await WriteEventAsync(context.Response, "changed", notification, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }

    private static async Task WriteEventAsync<T>(HttpResponse response, string eventName, T payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, s_jsonOptions);
        var frame = new StringBuilder()
            .Append("event: ").Append(eventName).Append('\n')
            .Append("data: ").Append(json).Append("\n\n")
            .ToString();
        await response.WriteAsync(frame, Encoding.UTF8, ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string ComputeEtag(Guid environmentId, DateTimeOffset? mostRecent)
    {
        var ticks = mostRecent.HasValue ? mostRecent.Value.UtcTicks : 0L;
        return $"\"{environmentId:N}-{ticks.ToString(CultureInfo.InvariantCulture)}\"";
    }

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null)
        { return b; }
        if (b is null)
        { return a; }
        return a.Value >= b.Value ? a : b;
    }
}

