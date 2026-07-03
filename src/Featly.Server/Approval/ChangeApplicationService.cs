using System.Text.Json;
using Featly.Server.Endpoints;
using Featly.Server.Telemetry;
using ChangeNotification = Featly.Storage.ChangeNotification;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Approval;

/// <summary>
/// Applies an approved (or emergency-bypassed) <see cref="PendingChange"/> to
/// its underlying entity. The change's <see cref="PendingChange.ProposedState"/>
/// is the entity's write-request JSON; this service deserializes it and routes
/// to the matching store, then emits the usual <see cref="ChangeNotification"/>
/// so connected SDKs invalidate their cached snapshots.
/// </summary>
internal sealed class ChangeApplicationService(StorageFacade store, FeatlyServerMetrics metrics)
{
    private static readonly JsonSerializerOptions s_json = ChangeJson.Options;

    /// <summary>Entity types this service knows how to apply.</summary>
    public static bool IsSupported(string entityType)
        => entityType is "Flag" or "Config" or "Segment";

    /// <summary>
    /// Applies the change's proposed state to the underlying entity. Returns
    /// <c>false</c> (without mutating) when the entity type is unknown or the
    /// proposed state can't be deserialized.
    /// </summary>
    public async Task<bool> ApplyAsync(PendingChange change, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);

        using var activity = metrics.ActivitySource.StartActivity("featly.change.apply");
        activity?.SetTag("featly.entity_type", change.EntityType);
        activity?.SetTag("featly.entity_key", change.EntityKey);
        activity?.SetTag("featly.change_action", change.Action.ToString());

        switch (change.EntityType)
        {
            case "Flag":
                await ApplyFlagAsync(change, actor, ct).ConfigureAwait(false);
                break;
            case "Config":
                await ApplyConfigAsync(change, actor, ct).ConfigureAwait(false);
                break;
            case "Segment":
                await ApplySegmentAsync(change, actor, ct).ConfigureAwait(false);
                break;
            default:
                return false;
        }

        await store.Changes.NotifyAsync(
            new ChangeNotification(change.EnvironmentId, change.EntityType, change.EntityKey, DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        return true;
    }

    private async Task ApplyFlagAsync(PendingChange change, string actor, CancellationToken ct)
    {
        if (change.Action == ChangeAction.Archive)
        {
            await store.Flags.ArchiveAsync(change.EnvironmentId, change.EntityKey, actor, ct).ConfigureAwait(false);
            return;
        }

        var body = change.ProposedState.Deserialize<FlagWriteRequest>(s_json)
            ?? throw new InvalidOperationException("Proposed flag state could not be deserialized.");
        var existing = await store.Flags.GetAsync(change.EnvironmentId, change.EntityKey, ct).ConfigureAwait(false);

        if (existing is null)
        {
            await store.Flags.UpsertAsync(change.EnvironmentId, body.ToEntity(change.EnvironmentId, actor), actor, ct).ConfigureAwait(false);
            return;
        }

        existing.Name = body.Name;
        existing.Description = body.Description;
        existing.Type = body.Type;
        existing.Enabled = body.Enabled;
        existing.DefaultVariantKey = body.DefaultVariantKey;
        existing.Variants = [.. body.Variants];
        existing.Tags = [.. (body.Tags ?? [])];
        existing.Rules = body.Rules is null ? [] : [.. body.Rules];
        existing.Prerequisites = body.Prerequisites is null ? [] : [.. body.Prerequisites];
        await store.Flags.UpsertAsync(change.EnvironmentId, existing, actor, ct).ConfigureAwait(false);
    }

    private async Task ApplyConfigAsync(PendingChange change, string actor, CancellationToken ct)
    {
        if (change.Action == ChangeAction.Archive)
        {
            await store.Configs.ArchiveAsync(change.EnvironmentId, change.EntityKey, actor, ct).ConfigureAwait(false);
            return;
        }

        var body = change.ProposedState.Deserialize<ConfigWriteRequest>(s_json)
            ?? throw new InvalidOperationException("Proposed config state could not be deserialized.");
        var existing = await store.Configs.GetAsync(change.EnvironmentId, change.EntityKey, ct).ConfigureAwait(false);

        if (existing is null)
        {
            await store.Configs.UpsertAsync(change.EnvironmentId, body.ToEntity(change.EnvironmentId, actor), actor, ct).ConfigureAwait(false);
            return;
        }

        existing.Name = body.Name;
        existing.Description = body.Description;
        existing.Type = body.Type;
        existing.DefaultValue = body.DefaultValue;
        existing.Tags = [.. (body.Tags ?? [])];
        existing.Rules = body.Rules is null ? [] : [.. body.Rules];
        await store.Configs.UpsertAsync(change.EnvironmentId, existing, actor, ct).ConfigureAwait(false);
    }

    private async Task ApplySegmentAsync(PendingChange change, string actor, CancellationToken ct)
    {
        if (change.Action == ChangeAction.Archive)
        {
            await store.Segments.DeleteAsync(change.EnvironmentId, change.EntityKey, actor, ct).ConfigureAwait(false);
            return;
        }

        var body = change.ProposedState.Deserialize<SegmentWriteRequest>(s_json)
            ?? throw new InvalidOperationException("Proposed segment state could not be deserialized.");
        var existing = await store.Segments.GetAsync(change.EnvironmentId, change.EntityKey, ct).ConfigureAwait(false);

        if (existing is null)
        {
            await store.Segments.UpsertAsync(change.EnvironmentId, body.ToEntity(change.EnvironmentId, actor), actor, ct).ConfigureAwait(false);
            return;
        }

        existing.Name = body.Name;
        existing.Description = body.Description;
        existing.Conditions = [.. body.Conditions];
        await store.Segments.UpsertAsync(change.EnvironmentId, existing, actor, ct).ConfigureAwait(false);
    }
}
