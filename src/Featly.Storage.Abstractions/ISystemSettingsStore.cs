namespace Featly.Storage;

/// <summary>
/// Persistence for DB-overridable settings singletons (ARCHITECTURE.md §15).
/// Each <see cref="SystemSetting"/> is a typed settings aggregate stored as one
/// row keyed by <see cref="SystemSetting.Key"/>. The store is untyped — the
/// server's settings provider owns the typed (de)serialization and the
/// three-layer precedence merge.
/// </summary>
public interface ISystemSettingsStore
{
    /// <summary>Returns the settings singleton for <paramref name="key"/>, or <c>null</c> if none is persisted.</summary>
    Task<SystemSetting?> GetAsync(string key, CancellationToken ct);

    /// <summary>Inserts or replaces the settings singleton (matched by <see cref="SystemSetting.Key"/>).</summary>
    Task UpsertAsync(SystemSetting setting, CancellationToken ct);

    /// <summary>Returns every persisted settings singleton, ordered by key.</summary>
    Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct);
}
