namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Config"/> entities, scoped per environment.
/// Mirrors <see cref="IFlagStore"/> — the two surfaces are intentionally parallel.
/// </summary>
public interface IConfigStore
{
    /// <summary>Returns the config with the given key, or <c>null</c> if missing.</summary>
    Task<Config?> GetAsync(Guid environmentId, string key, CancellationToken ct);

    /// <summary>Lists all non-archived configs in the environment.</summary>
    Task<IReadOnlyList<Config>> ListAsync(Guid environmentId, CancellationToken ct);

    /// <summary>Lists all archived configs in the environment.</summary>
    Task<IReadOnlyList<Config>> ListArchivedAsync(Guid environmentId, CancellationToken ct);

    /// <summary>
    /// Inserts or replaces the config. Bumps <see cref="Config.UpdatedAt"/> and
    /// <see cref="Config.UpdatedBy"/> server-side based on <paramref name="actor"/>.
    /// </summary>
    Task UpsertAsync(Guid environmentId, Config config, string actor, CancellationToken ct);

    /// <summary>Marks the config as archived. The row is retained for audit.</summary>
    Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct);

    /// <summary>Clears the archived flag, returning the config to the active list.</summary>
    Task UnarchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct);

    /// <summary>
    /// Returns the most recent <see cref="Config.UpdatedAt"/> across all configs
    /// in the environment, or <c>null</c> when there are none. Folded into the
    /// SDK config endpoint's ETag.
    /// </summary>
    Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct);
}
