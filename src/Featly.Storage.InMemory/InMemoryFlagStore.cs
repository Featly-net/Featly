using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryFlagStore : IFlagStore
{
    private readonly ConcurrentDictionary<(Guid EnvironmentId, string Key), Flag> _flags =
        new(EqualityComparer<(Guid, string)>.Default);

    public Task<Flag?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_flags.TryGetValue((environmentId, key.ToUpperInvariant()), out var flag) ? flag : null);
    }

    public Task<IReadOnlyList<Flag>> ListAsync(Guid environmentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Flag>>(
            [.. _flags.Values.Where(f => f.EnvironmentId == environmentId && !f.Archived)]);

    public Task UpsertAsync(Guid environmentId, Flag flag, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        flag.UpdatedAt = DateTimeOffset.UtcNow;
        flag.UpdatedBy = actor;

        _flags[(environmentId, flag.Key.ToUpperInvariant())] = flag;
        return Task.CompletedTask;
    }

    public Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var lookupKey = (environmentId, key.ToUpperInvariant());
        if (_flags.TryGetValue(lookupKey, out var existing))
        {
            existing.Archived = true;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actor;
        }
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct)
    {
        var snapshot = _flags.Values.Where(f => f.EnvironmentId == environmentId).ToList();
        if (snapshot.Count == 0)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }

        return Task.FromResult<DateTimeOffset?>(snapshot.Max(f => f.UpdatedAt));
    }
}
