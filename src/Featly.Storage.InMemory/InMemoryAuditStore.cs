using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryAuditStore : IAuditStore
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? entityType = null,
        string? entityKey = null,
        string? actorIdentifier = null,
        Guid? environmentId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        var list = _entries
            .Where(e => entityType is null || string.Equals(e.EntityType, entityType, StringComparison.Ordinal))
            .Where(e => entityKey is null || string.Equals(e.EntityKey, entityKey, StringComparison.Ordinal))
            .Where(e => actorIdentifier is null || string.Equals(e.ActorIdentifier, actorIdentifier, StringComparison.Ordinal))
            .Where(e => environmentId is null || e.EnvironmentId == environmentId)
            .Where(e => from is null || e.At >= from)
            .Where(e => to is null || e.At <= to)
            .OrderByDescending(e => e.At)
            .Take(limit <= 0 ? 200 : limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<AuditEntry>>(list);
    }
}
