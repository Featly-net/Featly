namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="RoleUpgradeRequest"/> entities.
/// </summary>
public interface IRoleUpgradeRequestStore
{
    /// <summary>Returns the request with the given row id, or <c>null</c>.</summary>
    Task<RoleUpgradeRequest?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lists every request. Sorted by creation time descending (newest first).</summary>
    Task<IReadOnlyList<RoleUpgradeRequest>> ListAsync(CancellationToken ct);

    /// <summary>Lists requests filtered by <see cref="RoleUpgradeStatus"/>. Sorted newest first.</summary>
    Task<IReadOnlyList<RoleUpgradeRequest>> ListByStatusAsync(RoleUpgradeStatus status, CancellationToken ct);

    /// <summary>Inserts a new request. Throws if the id collides.</summary>
    Task CreateAsync(RoleUpgradeRequest request, CancellationToken ct);

    /// <summary>
    /// Persists a decision (status + decider + comment + timestamp) on an
    /// existing request. Idempotent for a missing id (no-op).
    /// </summary>
    Task UpdateAsync(RoleUpgradeRequest request, CancellationToken ct);
}
