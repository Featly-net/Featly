namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Project"/> entities.
/// </summary>
public interface IProjectStore
{
    /// <summary>Returns the project with the given key, or <c>null</c> if missing.</summary>
    Task<Project?> GetByKeyAsync(string key, CancellationToken ct);

    /// <summary>Returns the project with the given id, or <c>null</c> if missing.</summary>
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Returns the default project, or <c>null</c> if none exists yet.</summary>
    Task<Project?> GetDefaultAsync(CancellationToken ct);

    /// <summary>Lists every project.</summary>
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct);

    /// <summary>Inserts a new project. Throws if a project with the same key already exists.</summary>
    Task CreateAsync(Project project, CancellationToken ct);

    /// <summary>Updates a project's mutable metadata (name, description). The key is immutable.</summary>
    Task UpdateAsync(Project project, CancellationToken ct);
}
