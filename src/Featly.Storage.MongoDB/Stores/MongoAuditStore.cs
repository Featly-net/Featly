using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoAuditStore(MongoFeatlyDatabase database) : IAuditStore
{
    // Serializes appends process-wide so the read-tail -> chain -> insert
    // sequence is atomic and the chain stays linear — the same single-writer
    // embedded-deployment assumption every relational provider's EfAuditStore
    // makes (see ADR-0030); concurrent writers across instances would need
    // DB-level coordination, which none of the providers implement today.
    private static readonly SemaphoreSlim s_appendGate = new(1, 1);

    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await s_appendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tail = await database.AuditEntries
                .Find(FilterDefinition<AuditEntry>.Empty)
                .SortByDescending(a => a.Sequence)
                .Limit(1)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            // BSON's native Date type is millisecond precision (a hard
            // BSON-spec limit, unlike the relational providers' microsecond+
            // datetime columns) — AuditHash.Compute folds At.UtcTicks into the
            // digest, so hashing the caller's full-precision At and then
            // storing it would make VerifyChainAsync's later recompute (which
            // reads the driver-truncated At back) mismatch on every entry.
            // Hash and store the truncated value so both sides agree.
            var stored = new AuditEntry
            {
                Id = entry.Id,
                At = TruncateToMilliseconds(entry.At),
                Action = entry.Action,
                EntityType = entry.EntityType,
                EntityKey = entry.EntityKey,
                EnvironmentId = entry.EnvironmentId,
                ActorIdentifier = entry.ActorIdentifier,
                Data = entry.Data,
                Sequence = (tail?.Sequence ?? 0) + 1,
                PreviousHash = tail?.Hash,
            };
            stored.Hash = AuditHash.Compute(stored, stored.PreviousHash);

            await database.AuditEntries.InsertOneAsync(stored, cancellationToken: ct).ConfigureAwait(false);

            entry.Sequence = stored.Sequence;
            entry.PreviousHash = stored.PreviousHash;
            entry.Hash = stored.Hash;
        }
        finally
        {
            s_appendGate.Release();
        }
    }

    public async Task<AuditChainVerification> VerifyChainAsync(CancellationToken ct)
    {
        var entries = await database.AuditEntries
            .Find(a => a.Hash != null)
            .SortBy(a => a.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return AuditChainVerifier.Verify(entries);
    }

    public async Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var result = await database.AuditEntries.DeleteManyAsync(a => a.At < cutoff, ct).ConfigureAwait(false);
        return (int)result.DeletedCount;
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? entityType = null,
        string? entityKey = null,
        string? actorIdentifier = null,
        Guid? environmentId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        var filter = FilterDefinition<AuditEntry>.Empty;

        if (entityType is not null)
        {
            filter &= Builders<AuditEntry>.Filter.Eq(a => a.EntityType, entityType);
        }

        if (entityKey is not null)
        {
            filter &= Builders<AuditEntry>.Filter.Eq(a => a.EntityKey, entityKey);
        }

        if (actorIdentifier is not null)
        {
            filter &= Builders<AuditEntry>.Filter.Eq(a => a.ActorIdentifier, actorIdentifier);
        }

        if (environmentId is not null)
        {
            filter &= Builders<AuditEntry>.Filter.Eq(a => a.EnvironmentId, environmentId);
        }

        if (from is not null)
        {
            filter &= Builders<AuditEntry>.Filter.Gte(a => a.At, from.Value);
        }

        if (to is not null)
        {
            filter &= Builders<AuditEntry>.Filter.Lte(a => a.At, to.Value);
        }

        return await database.AuditEntries
            .Find(filter)
            .SortByDescending(a => a.At)
            .Limit(limit <= 0 ? 200 : limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), value.Offset);
}
