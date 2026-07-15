using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryAuditStore : IAuditStore
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Chain the entry (issue #208) under the same lock that guards pruning, so
        // the read-tail -> chain -> enqueue sequence is atomic and the chain stays
        // linear.
        lock (_entries)
        {
            var tail = _entries.LastOrDefault();
            entry.Sequence = (tail?.Sequence ?? 0) + 1;
            entry.PreviousHash = tail?.Hash;
            entry.Hash = AuditHash.Compute(entry, entry.PreviousHash);
            _entries.Enqueue(entry);
        }
        return Task.CompletedTask;
    }

    public Task<AuditChainVerification> VerifyChainAsync(CancellationToken ct)
    {
        List<AuditEntry> ordered;
        lock (_entries)
        {
            ordered = _entries.Where(e => e.Hash is not null).OrderBy(e => e.Sequence).ToList();
        }
        return Task.FromResult(AuditChainVerifier.Verify(ordered));
    }

    public Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        // Drain, keep the survivors (in order), re-enqueue. The lock guards
        // against an interleaved Append losing an entry.
        lock (_entries)
        {
            var kept = new List<AuditEntry>(_entries.Count);
            var removed = 0;
            while (_entries.TryDequeue(out var entry))
            {
                if (entry.At < cutoff)
                { removed++; }
                else
                { kept.Add(entry); }
            }
            foreach (var e in kept)
            { _entries.Enqueue(e); }
            return Task.FromResult(removed);
        }
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
