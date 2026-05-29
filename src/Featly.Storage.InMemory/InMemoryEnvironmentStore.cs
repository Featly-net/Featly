using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryEnvironmentStore : IEnvironmentStore
{
    private readonly ConcurrentDictionary<Guid, Environment> _byId = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, string Key), Guid> _idByProjectKey =
        new(EqualityComparer<(Guid, string)>.Default);

    public Task<Environment?> GetByKeyAsync(Guid projectId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var lookupKey = (projectId, key.ToUpperInvariant());
        return Task.FromResult(_idByProjectKey.TryGetValue(lookupKey, out var id) && _byId.TryGetValue(id, out var env)
            ? env
            : null);
    }

    public Task<Environment?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var env) ? env : null);

    public Task<Environment?> GetDefaultAsync(Guid projectId, CancellationToken ct)
        => Task.FromResult<Environment?>(_byId.Values.FirstOrDefault(e => e.ProjectId == projectId && e.IsDefault));

    public Task<IReadOnlyList<Environment>> ListAsync(Guid projectId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Environment>>([.. _byId.Values.Where(e => e.ProjectId == projectId)]);

    public Task CreateAsync(Environment environment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var lookupKey = (environment.ProjectId, environment.Key.ToUpperInvariant());
        if (!_idByProjectKey.TryAdd(lookupKey, environment.Id))
        {
            throw new InvalidOperationException(
                $"An environment with key '{environment.Key}' already exists in project '{environment.ProjectId}'.");
        }

        _byId[environment.Id] = environment;
        return Task.CompletedTask;
    }

    public Task<Environment?> SetReadOnlyAsync(Guid id, bool readOnly, CancellationToken ct)
    {
        if (_byId.TryGetValue(id, out var env))
        {
            env.ReadOnly = readOnly;
            return Task.FromResult<Environment?>(env);
        }
        return Task.FromResult<Environment?>(null);
    }
}
