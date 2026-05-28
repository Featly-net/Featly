using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteAuditStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IAuditStore
{
    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
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

        var query = db.AuditEntries.AsNoTracking().AsQueryable();

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
