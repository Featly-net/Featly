using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteSegmentStore(IDbContextFactory<FeatlyDbContext> contextFactory) : ISegmentStore
{
    public async Task<Segment?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Segments.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EnvironmentId == environmentId && s.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Segment>> ListAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Segments.AsNoTracking()
            .Where(s => s.EnvironmentId == environmentId)
            .OrderBy(s => s.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(Guid environmentId, Segment segment, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        segment.UpdatedAt = DateTimeOffset.UtcNow;
        segment.UpdatedBy = actor;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Segments
            .Include(s => s.Conditions)
            .FirstOrDefaultAsync(s => s.EnvironmentId == environmentId && s.Key == segment.Key, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Segments.Add(segment);
        }
        else
        {
            existing.Name = segment.Name;
            existing.Description = segment.Description;
            existing.UpdatedAt = segment.UpdatedAt;
            existing.UpdatedBy = segment.UpdatedBy;

            existing.Conditions.Clear();
            foreach (var c in segment.Conditions)
            {
                existing.Conditions.Add(c);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Segments
            .FirstOrDefaultAsync(s => s.EnvironmentId == environmentId && s.Key == key, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        db.Segments.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // SQLite cannot ORDER BY DateTimeOffset (TEXT column), so aggregate client-side
        // for the same reasons documented in SqliteFlagStore.GetMostRecentUpdateAsync.
        var timestamps = await db.Segments.AsNoTracking()
            .Where(s => s.EnvironmentId == environmentId)
            .Select(s => s.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return timestamps.Count == 0 ? null : timestamps.Max();
    }
}
