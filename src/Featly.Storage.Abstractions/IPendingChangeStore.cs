namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="PendingChange"/> entities. Approvals
/// and comments are stored inline on the change (owned collections).
/// </summary>
public interface IPendingChangeStore
{
    /// <summary>Returns the change with the given row id, or <c>null</c>.</summary>
    Task<PendingChange?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lists every change. Sorted by creation time descending (newest first).</summary>
    Task<IReadOnlyList<PendingChange>> ListAsync(CancellationToken ct);

    /// <summary>Lists changes filtered by <see cref="ChangeStatus"/>. Sorted newest first.</summary>
    Task<IReadOnlyList<PendingChange>> ListByStatusAsync(ChangeStatus status, CancellationToken ct);

    /// <summary>Lists changes targeting a given environment. Sorted newest first.</summary>
    Task<IReadOnlyList<PendingChange>> ListByEnvironmentAsync(Guid environmentId, CancellationToken ct);

    /// <summary>
    /// Lists the still-open changes (<see cref="ChangeStatus.Pending"/> or
    /// <see cref="ChangeStatus.Approved"/>) that target a specific entity.
    /// Used to mark sibling changes stale when one applies.
    /// </summary>
    Task<IReadOnlyList<PendingChange>> ListOpenForEntityAsync(string entityType, string entityKey, Guid environmentId, CancellationToken ct);

    /// <summary>Inserts a new change. Throws if the id collides.</summary>
    Task CreateAsync(PendingChange change, CancellationToken ct);

    /// <summary>
    /// Persists the mutable parts of a change (status, approvals, comments,
    /// applied / rejected fields). Idempotent for a missing id (no-op).
    /// </summary>
    Task UpdateAsync(PendingChange change, CancellationToken ct);

    /// <summary>
    /// Atomically transitions the change's status from <paramref name="from"/> to
    /// <paramref name="to"/> only if it is currently <paramref name="from"/>,
    /// returning whether this call performed the transition. Lets a background
    /// worker claim a change exactly once even when several instances run against
    /// a shared database (issue #237): the first caller wins, the rest get
    /// <c>false</c> and skip.
    /// </summary>
    Task<bool> TryClaimStatusAsync(Guid id, ChangeStatus from, ChangeStatus to, CancellationToken ct);
}
