using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteEventStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IEventStore
{
    public async Task AppendAsync(IReadOnlyList<Event> events, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.Events.AddRange(events);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Event>> QueryAsync(
        Guid environmentId,
        EventType? type = null,
        string? flagKey = null,
        string? customKey = null,
        CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var query = db.Events.AsNoTracking().Where(e => e.EnvironmentId == environmentId);

        if (type is not null)
        {
            query = query.Where(e => e.Type == type);
        }

        if (flagKey is not null)
        {
            query = query.Where(e => e.FlagKey == flagKey);
        }

        if (customKey is not null)
        {
            query = query.Where(e => e.CustomKey == customKey);
        }

        return await query.OrderBy(e => e.At).ToListAsync(ct).ConfigureAwait(false);
    }
}
