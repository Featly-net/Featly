using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Featly.Server.Authentication;
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

    private static async Task GetConfigAsync(HttpContext context, StorageFacade store, string? env, CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new { error = $"Environment '{env}' not found." }, ct).ConfigureAwait(false);
            return;
        }

        var mostRecent = await store.Flags.GetMostRecentUpdateAsync(environment.Id, ct).ConfigureAwait(false);
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
        var snapshot = new ConfigSnapshot(
            EnvironmentId: environment.Id,
            EnvironmentKey: environment.Key,
            At: DateTimeOffset.UtcNow,
            Flags: flags);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await JsonSerializer.SerializeAsync(context.Response.Body, snapshot, s_jsonOptions, ct).ConfigureAwait(false);
    }

    // ConfigSnapshot record moved to Featly.Abstractions so the SDK shares the same shape.

    private static async Task StreamAsync(HttpContext context, StorageFacade store, string? env, CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

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

    private static async Task<Environment?> ResolveEnvironmentAsync(StorageFacade store, string? envKey, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(envKey)
            ? await store.Environments.GetDefaultAsync(project.Id, ct).ConfigureAwait(false)
            : await store.Environments.GetByKeyAsync(project.Id, envKey, ct).ConfigureAwait(false);
    }

    private static string ComputeEtag(Guid environmentId, DateTimeOffset? mostRecent)
    {
        var ticks = mostRecent.HasValue ? mostRecent.Value.UtcTicks : 0L;
        return $"\"{environmentId:N}-{ticks.ToString(CultureInfo.InvariantCulture)}\"";
    }
}

