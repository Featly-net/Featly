using System.Text.Json;
using Featly.Server.Authentication;
using Featly.Server.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// SDK-facing telemetry ingest (ARCHITECTURE.md section 16). The SDK batches
/// automatic exposure events and application-tracked custom events and flushes
/// them here. Behind the SDK auth policy; rows are stamped with the resolved
/// environment and a server-assigned id before being appended.
/// </summary>
internal static class SdkEventsEndpoints
{
    public static RouteGroupBuilder MapSdkEvents(this RouteGroupBuilder group)
    {
        var sdk = group.MapGroup("/sdk").RequireAuthorization(FeatlyAuthenticationDefaults.SdkPolicy);

        sdk.MapPost("/events", IngestAsync).WithName("Featly.Sdk.Events.Ingest");

        return group;
    }

    private static async Task<IResult> IngestAsync(
        EventBatchRequest body,
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

        if (body.Events is null || body.Events.Count == 0)
        {
            return Results.Accepted(value: new { ingested = 0 });
        }

        var now = DateTimeOffset.UtcNow;
        var events = new List<Event>(body.Events.Count);
        foreach (var dto in body.Events)
        {
            if (string.IsNullOrWhiteSpace(dto.SubjectKey))
            {
                return Results.BadRequest(new { error = "Every event requires a subjectKey." });
            }

            events.Add(new Event
            {
                Id = Guid.NewGuid(),
                Type = dto.Type,
                FlagKey = dto.FlagKey,
                ConfigKey = dto.ConfigKey,
                CustomKey = dto.CustomKey,
                SubjectKey = dto.SubjectKey,
                VariantKey = dto.VariantKey,
                Properties = dto.Properties,
                At = dto.At ?? now,
                EnvironmentId = environment.Id,
            });
        }

        await store.Events.AppendAsync(events, ct).ConfigureAwait(false);

        // Count ingested events per type for the featly.server.events_ingested
        // counter (one Add per distinct type keeps the tag set bounded).
        var exposures = 0L;
        var customs = 0L;
        foreach (var e in events)
        {
            if (e.Type == EventType.Exposure)
            {
                exposures++;
            }
            else
            {
                customs++;
            }
        }
        metrics.RecordEventsIngested(EventType.Exposure, exposures);
        metrics.RecordEventsIngested(EventType.Custom, customs);

        return Results.Accepted(value: new { ingested = events.Count });
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

/// <summary>A batch of telemetry events posted by the SDK.</summary>
public sealed record EventBatchRequest(IReadOnlyList<EventIngestRequest> Events);

/// <summary>
/// Inbound shape for a single event. Id and environment are assigned server-side
/// on ingest; <c>at</c> defaults to the server clock when omitted.
/// </summary>
public sealed record EventIngestRequest(
    EventType Type,
    string SubjectKey,
    string? FlagKey = null,
    string? ConfigKey = null,
    string? CustomKey = null,
    string? VariantKey = null,
    Dictionary<string, JsonElement>? Properties = null,
    DateTimeOffset? At = null);
