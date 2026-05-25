namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Flag"/> entities, scoped per environment.
/// </summary>
public interface IFlagStore
{
    /// <summary>Returns the flag with the given key, or <c>null</c> if missing.</summary>
    Task<Flag?> GetAsync(Guid environmentId, string key, CancellationToken ct);

    /// <summary>Lists all non-archived flags in the environment.</summary>
    Task<IReadOnlyList<Flag>> ListAsync(Guid environmentId, CancellationToken ct);

    /// <summary>
    /// Inserts or replaces the flag. Bumps <see cref="Flag.UpdatedAt"/> and
    /// <see cref="Flag.UpdatedBy"/> server-side based on <paramref name="actor"/>.
    /// </summary>
    Task UpsertAsync(Guid environmentId, Flag flag, string actor, CancellationToken ct);

    /// <summary>Marks the flag as archived. The row is retained for audit.</summary>
    Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct);

    /// <summary>
    /// Returns the most recent <see cref="Flag.UpdatedAt"/> across all flags
    /// in the environment, or <c>null</c> when there are none. Used by the
    /// SDK config endpoint to compute its ETag.
    /// </summary>
    Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct);
}
