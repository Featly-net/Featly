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
            [.. _segments.Values.Where(s => s.EnvironmentId == environmentId && !s.Archived)]);

    public Task<IReadOnlyList<Segment>> ListArchivedAsync(Guid environmentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Segment>>(
            [.. _segments.Values.Where(s => s.EnvironmentId == environmentId && s.Archived)]);

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

    public Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (_segments.TryGetValue((environmentId, key.ToUpperInvariant()), out var existing))
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

        if (_segments.TryGetValue((environmentId, key.ToUpperInvariant()), out var existing))
        {
            existing.Archived = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actor;
        }
        return Task.CompletedTask;
    }

}
