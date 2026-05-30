using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryProjectStore : IProjectStore
{
    private readonly ConcurrentDictionary<Guid, Project> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _idByKey = new(StringComparer.OrdinalIgnoreCase);

    public Task<Project?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_idByKey.TryGetValue(key, out var id) && _byId.TryGetValue(id, out var project)
            ? project
            : null);
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var project) ? project : null);

    public Task<Project?> GetDefaultAsync(CancellationToken ct)
        => Task.FromResult<Project?>(_byId.Values.FirstOrDefault(p => p.IsDefault));

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Project>>([.. _byId.Values]);

    public Task CreateAsync(Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!_idByKey.TryAdd(project.Key, project.Id))
        {
            throw new InvalidOperationException($"A project with key '{project.Key}' already exists.");
        }

        _byId[project.Id] = project;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!_byId.ContainsKey(project.Id))
        {
            throw new InvalidOperationException($"Project '{project.Key}' not found.");
        }

        _byId[project.Id] = project;
        return Task.CompletedTask;
    }
}
