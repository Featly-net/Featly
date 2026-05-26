using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemorySegmentStore : ISegmentStore
{
    private readonly ConcurrentDictionary<(Guid EnvironmentId, string Key), Segment> _segments =
        new(EqualityComparer<(Guid, string)>.Default);

    public Task<Segment?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_segments.TryGetValue((environmentId, key.ToUpperInvariant()), out var s) ? s : null);
    }

    public Task<IReadOnlyList<Segment>> ListAsync(Guid environmentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Segment>>(
            [.. _segments.Values.Where(s => s.EnvironmentId == environmentId)]);

    public Task UpsertAsync(Guid environmentId, Segment segment, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        segment.UpdatedAt = DateTimeOffset.UtcNow;
        segment.UpdatedBy = actor;

        _segments[(environmentId, segment.Key.ToUpperInvariant())] = segment;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        _segments.TryRemove((environmentId, key.ToUpperInvariant()), out _);
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct)
    {
        var snapshot = _segments.Values.Where(s => s.EnvironmentId == environmentId).ToList();
        if (snapshot.Count == 0)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }
        return Task.FromResult<DateTimeOffset?>(snapshot.Max(s => s.UpdatedAt));
    }
}
