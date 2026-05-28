using System.Text.Json;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Approval;

/// <summary>
/// Detects when an approved <see cref="PendingChange"/> has gone stale because
/// the underlying entity changed between propose and apply (ARCHITECTURE.md
/// §12, step 5). Comparison is by the entity's <c>updatedAt</c> captured in the
/// change's <see cref="PendingChange.CurrentState"/> snapshot, so unrelated
/// edits invalidate the change and force a rebase.
/// </summary>
internal static class ChangeStaleness
{
    /// <summary>
    /// Returns <c>true</c> when the change can no longer be safely applied: a
    /// Create whose target now exists, or an Update/Archive whose target was
    /// deleted or edited since the change was proposed.
    /// </summary>
    public static async Task<bool> IsStaleAsync(StorageFacade store, PendingChange change, CancellationToken ct)
    {
        var live = await LoadStateAsync(store, change.EntityType, change.EntityKey, change.EnvironmentId, ct).ConfigureAwait(false);

        if (change.Action == ChangeAction.Create)
        {
            // Proposed to create, but it already exists now -> someone beat us to it.
            return live is not null;
        }

        // Update / Archive: target must still exist and be unchanged since propose.
        if (live is null)
        {
            return true;
        }
        var baseUpdatedAt = ReadUpdatedAt(change.CurrentState);
        var liveUpdatedAt = ReadUpdatedAt(live);
        return baseUpdatedAt != liveUpdatedAt;
    }

    /// <summary>
    /// Marks every other still-open change for the same entity as
    /// <see cref="ChangeStatus.Stale"/> after one of them applies, so the
    /// dashboard prompts their authors to rebase.
    /// </summary>
    public static async Task MarkSiblingsStaleAsync(StorageFacade store, PendingChange applied, CancellationToken ct)
    {
        var siblings = await store.PendingChanges
            .ListOpenForEntityAsync(applied.EntityType, applied.EntityKey, applied.EnvironmentId, ct)
            .ConfigureAwait(false);

        foreach (var sibling in siblings)
        {
            if (sibling.Id == applied.Id)
            {
                continue;
            }
            sibling.Status = ChangeStatus.Stale;
            sibling.UpdatedAt = DateTimeOffset.UtcNow;
            await store.PendingChanges.UpdateAsync(sibling, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serializes the live entity (Flag / Config / Segment) to JSON for a
    /// change's <see cref="PendingChange.CurrentState"/> snapshot, or returns
    /// <c>null</c> when it does not exist. Uses <see cref="ChangeJson.Options"/>
    /// so the captured shape matches what the staleness check reads back.
    /// </summary>
    public static async Task<JsonElement?> CaptureAsync(StorageFacade store, string entityType, string entityKey, Guid environmentId, CancellationToken ct)
        => await LoadStateAsync(store, entityType, entityKey, environmentId, ct).ConfigureAwait(false);

    private static async Task<JsonElement?> LoadStateAsync(StorageFacade store, string entityType, string entityKey, Guid environmentId, CancellationToken ct)
    {
        object? entity = entityType switch
        {
            "Flag" => await store.Flags.GetAsync(environmentId, entityKey, ct).ConfigureAwait(false),
            "Config" => await store.Configs.GetAsync(environmentId, entityKey, ct).ConfigureAwait(false),
            "Segment" => await store.Segments.GetAsync(environmentId, entityKey, ct).ConfigureAwait(false),
            _ => null,
        };
        return entity is null ? null : JsonSerializer.SerializeToElement(entity, entity.GetType(), ChangeJson.Options);
    }

    private static string? ReadUpdatedAt(JsonElement? state)
        => state is { } s && s.TryGetProperty("updatedAt", out var prop) ? prop.GetRawText() : null;
}
