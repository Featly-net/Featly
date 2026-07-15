using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.EntityFramework;

/// <summary>
/// Provider-agnostic <see cref="IAuditStore"/> implemented over EF Core, including
/// the tamper-evident hash chain (issue #208). The relational providers (SQLite,
/// Postgres) derive a one-line subclass bound to their own <typeparamref name="TContext"/>.
/// Compiled into each provider assembly as a linked source file (ADR-0026); every
/// query uses <c>Set&lt;AuditEntry&gt;()</c> so it stays context-agnostic.
/// </summary>
internal abstract class EfAuditStore<TContext>(IDbContextFactory<TContext> contextFactory) : IAuditStore
    where TContext : DbContext
{
    // Serializes appends process-wide so the read-tail -> chain -> insert sequence
    // is atomic and the chain stays linear. Correct for the single-writer embedded
    // deployment; concurrent writers across instances would need DB-level
    // coordination (see ADR-0030).
    private static readonly SemaphoreSlim s_appendGate = new(1, 1);

    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await s_appendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var tail = await db.Set<AuditEntry>().AsNoTracking()
                .OrderByDescending(a => a.Sequence)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            entry.Sequence = (tail?.Sequence ?? 0) + 1;
            entry.PreviousHash = tail?.Hash;
            entry.Hash = AuditHash.Compute(entry, entry.PreviousHash);

            db.Set<AuditEntry>().Add(entry);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            s_appendGate.Release();
        }
    }

    public async Task<AuditChainVerification> VerifyChainAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entries = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.Hash != null)
            .OrderBy(a => a.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return AuditChainVerifier.Verify(entries);
    }

    public async Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<AuditEntry>().Where(a => a.At < cutoff).ExecuteDeleteAsync(ct).ConfigureAwait(false);
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
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var query = db.Set<AuditEntry>().AsNoTracking().AsQueryable();

        if (entityType is not null)
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (entityKey is not null)
        {
            query = query.Where(a => a.EntityKey == entityKey);
        }

        if (actorIdentifier is not null)
        {
            query = query.Where(a => a.ActorIdentifier == actorIdentifier);
        }

        if (environmentId is not null)
        {
            query = query.Where(a => a.EnvironmentId == environmentId);
        }

        if (from is not null)
        {
            query = query.Where(a => a.At >= from);
        }

        if (to is not null)
        {
            query = query.Where(a => a.At <= to);
        }

        return await query
            .OrderByDescending(a => a.At)
            .Take(limit <= 0 ? 200 : limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
