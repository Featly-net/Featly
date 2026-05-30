using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryConfigStore : IConfigStore
{
    private readonly ConcurrentDictionary<(Guid EnvironmentId, string Key), Config> _configs =
        new(EqualityComparer<(Guid, string)>.Default);

    public Task<Config?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_configs.TryGetValue((environmentId, key.ToUpperInvariant()), out var c) ? c : null);
    }

    public Task<IReadOnlyList<Config>> ListAsync(Guid environmentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Config>>(
            [.. _configs.Values.Where(c => c.EnvironmentId == environmentId && !c.Archived)]);

    public Task<IReadOnlyList<Config>> ListArchivedAsync(Guid environmentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Config>>(
            [.. _configs.Values.Where(c => c.EnvironmentId == environmentId && c.Archived)]);

    public Task UpsertAsync(Guid environmentId, Config config, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        config.UpdatedAt = DateTimeOffset.UtcNow;
        config.UpdatedBy = actor;

        _configs[(environmentId, config.Key.ToUpperInvariant())] = config;
        return Task.CompletedTask;
    }

    public Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var lookupKey = (environmentId, key.ToUpperInvariant());
        if (_configs.TryGetValue(lookupKey, out var existing))
        {
            existing.Archived = true;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actor;
        }
        return Task.CompletedTask;
    }

    public Task UnarchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var lookupKey = (environmentId, key.ToUpperInvariant());
        if (_configs.TryGetValue(lookupKey, out var existing))
        {
            existing.Archived = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actor;
        }
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct)
    {
        var snapshot = _configs.Values.Where(c => c.EnvironmentId == environmentId).ToList();
        return Task.FromResult<DateTimeOffset?>(
            snapshot.Count == 0 ? null : snapshot.Max(c => c.UpdatedAt));
    }
}
