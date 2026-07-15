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

    /// <summary>Lists all archived flags in the environment.</summary>
    Task<IReadOnlyList<Flag>> ListArchivedAsync(Guid environmentId, CancellationToken ct);

    /// <summary>
    /// Inserts or replaces the flag. Bumps <see cref="Flag.UpdatedAt"/> and
    /// <see cref="Flag.UpdatedBy"/> server-side based on <paramref name="actor"/>.
    /// </summary>
    Task UpsertAsync(Guid environmentId, Flag flag, string actor, CancellationToken ct);

    /// <summary>Marks the flag as archived. The row is retained for audit.</summary>
    Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct);

    /// <summary>Clears the archived flag, returning the flag to the active list.</summary>
    Task UnarchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct);

}
