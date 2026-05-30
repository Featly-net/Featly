namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Environment"/> entities.
/// </summary>
public interface IEnvironmentStore
{
    /// <summary>Returns the environment with the given key inside the project, or <c>null</c>.</summary>
    Task<Environment?> GetByKeyAsync(Guid projectId, string key, CancellationToken ct);

    /// <summary>Returns the environment with the given id, or <c>null</c> if missing.</summary>
    Task<Environment?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Returns the default environment for the project, or <c>null</c>.</summary>
    Task<Environment?> GetDefaultAsync(Guid projectId, CancellationToken ct);

    /// <summary>Lists every environment in the project.</summary>
    Task<IReadOnlyList<Environment>> ListAsync(Guid projectId, CancellationToken ct);

    /// <summary>Inserts a new environment. Throws on duplicate <c>(projectId, key)</c>.</summary>
    Task CreateAsync(Environment environment, CancellationToken ct);

    /// <summary>Updates an environment's mutable metadata (name). The key is immutable.</summary>
    Task UpdateAsync(Environment environment, CancellationToken ct);

    /// <summary>Deletes the environment matched by id. The caller guards the default / non-empty cases.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Sets the <see cref="Environment.ReadOnly"/> freeze flag on the environment
    /// matched by id. Returns the updated environment, or <c>null</c> if missing.
    /// </summary>
    Task<Environment?> SetReadOnlyAsync(Guid id, bool readOnly, CancellationToken ct);
}
