using System.Text.Json;
using Featly.Engine;
using Featly.Server.Authentication;
using Featly.Server.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin "test this context" preview endpoints. The dashboard uses these to
/// dry-run an evaluation server-side — given a candidate
/// <see cref="EvaluationContext"/>, return the full
/// <see cref="EvaluationResult{JsonElement}"/> without persisting anything.
/// </summary>
/// <remarks>
/// <para>
/// Configs and flags are evaluated through the same <see cref="Evaluator"/> the
/// SDK uses locally. Segments from the same environment are loaded and handed
/// to the engine as an <see cref="ISegmentLookup"/>, so <c>InSegment</c>
/// conditions resolve correctly.
/// </para>
/// <para>
/// The request body's <c>attributes</c> arrive as a
/// <c>Dictionary&lt;string, JsonElement&gt;</c>; each value is unwrapped to
/// the .NET primitive the engine's comparators expect (string, double, bool,
/// array). Authors don't have to think about JSON-vs-CLR types in the form.
/// </para>
/// </remarks>
internal static class AdminPreviewEndpoints
{
    public static RouteGroupBuilder MapAdminPreview(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/preview").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapPost("/flags/{key}", PreviewFlagAsync).WithName("Featly.Admin.Preview.Flag").RequirePermission(Permission.FlagRead);
        admin.MapPost("/configs/{key}", PreviewConfigAsync).WithName("Featly.Admin.Preview.Config").RequirePermission(Permission.ConfigRead);

        return group;
    }

    private static async Task<IResult> PreviewFlagAsync(
        string key,
        PreviewRequest body,
        StorageFacade store,
        FeatlyServerMetrics metrics,
        string? env,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var flag = await store.Flags.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (flag is null)
        {
            return Results.NotFound(new { error = $"Flag '{key}' not found." });
        }

        var segments = await store.Segments.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var lookup = BuildSegmentLookup(segments);
        var context = body.ToEvaluationContext();
        var fallback = JsonSerializer.SerializeToElement<object?>(null);

        var result = Evaluator.EvaluateFlag(flag, context, fallback, lookup);
        metrics.RecordEvaluation("flag", result.Reason.ToString());
        return Results.Ok(result);
    }

    private static async Task<IResult> PreviewConfigAsync(
        string key,
        PreviewRequest body,
        StorageFacade store,
        FeatlyServerMetrics metrics,
        string? env,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var config = await store.Configs.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (config is null)
        {
            return Results.NotFound(new { error = $"Config '{key}' not found." });
        }

        var segments = await store.Segments.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var lookup = BuildSegmentLookup(segments);
        var context = body.ToEvaluationContext();
        var fallback = JsonSerializer.SerializeToElement<object?>(null);

        var result = Evaluator.EvaluateConfig(config, context, fallback, lookup);
        metrics.RecordEvaluation("config", result.Reason.ToString());
        return Results.Ok(result);
    }

    private static DictionarySegmentLookup BuildSegmentLookup(IReadOnlyList<Segment> segments)
    {
        var map = new Dictionary<string, Segment>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            map[segment.Key] = segment;
        }
        return new DictionarySegmentLookup(map);
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
}

/// <summary>
/// Body of a preview request. Mirrors <see cref="EvaluationContext"/> on the
/// wire but takes <see cref="JsonElement"/> attribute values so callers can
/// send them in their natural JSON form (string / number / bool / array).
/// </summary>
public sealed record PreviewRequest(
    string? TargetingKey = null,
    IReadOnlyDictionary<string, JsonElement>? Attributes = null)
{
    internal EvaluationContext ToEvaluationContext()
    {
        if (Attributes is null || Attributes.Count == 0)
        {
            return new EvaluationContext(TargetingKey: TargetingKey, Attributes: null);
        }

        var unwrapped = new Dictionary<string, object?>(Attributes.Count, StringComparer.Ordinal);
        foreach (var (k, v) in Attributes)
        {
            unwrapped[k] = UnwrapJson(v);
        }
        return new EvaluationContext(TargetingKey: TargetingKey, Attributes: unwrapped);
    }

    private static object? UnwrapJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                { return l; }
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Array:
                var list = new List<object?>(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(UnwrapJson(item));
                }
                return list;
            default:
                // Objects pass through as JsonElement; the engine's comparators
                // only know scalars and arrays, so a nested object always
                // misses — which is correct for now.
                return element;
        }
    }
}
