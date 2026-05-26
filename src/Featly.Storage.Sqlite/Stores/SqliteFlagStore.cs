using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteFlagStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IFlagStore
{
    public async Task<Flag?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Flags.AsNoTracking()
            .FirstOrDefaultAsync(f => f.EnvironmentId == environmentId && f.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Flag>> ListAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Flags.AsNoTracking()
            .Where(f => f.EnvironmentId == environmentId && !f.Archived)
            .OrderBy(f => f.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(Guid environmentId, Flag flag, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        flag.UpdatedAt = DateTimeOffset.UtcNow;
        flag.UpdatedBy = actor;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Flags
            .Include(f => f.Variants)
            .FirstOrDefaultAsync(f => f.EnvironmentId == environmentId && f.Key == flag.Key, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Flags.Add(flag);
        }
        else
        {
            // Copy mutable fields; Id/Key/EnvironmentId/CreatedAt/CreatedBy stay put.
            existing.Name = flag.Name;
            existing.Description = flag.Description;
            existing.Type = flag.Type;
            existing.Enabled = flag.Enabled;
            existing.DefaultVariantKey = flag.DefaultVariantKey;
            existing.Tags = flag.Tags;
            existing.Archived = flag.Archived;
            existing.UpdatedAt = flag.UpdatedAt;
            existing.UpdatedBy = flag.UpdatedBy;

            // Replace the owned JSON collection in-place. EF Core diffs the
            // serialized JSON column on SaveChanges.
            existing.Variants.Clear();
            foreach (var variant in flag.Variants)
            {
                existing.Variants.Add(variant);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Flags
            .FirstOrDefaultAsync(f => f.EnvironmentId == environmentId && f.Key == key, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        existing.Archived = true;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedBy = actor;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // UpdatedAt is persisted as UTC ticks (long), so MAX runs in SQL.
        return await db.Flags.AsNoTracking()
            .Where(f => f.EnvironmentId == environmentId)
            .Select(f => (DateTimeOffset?)f.UpdatedAt)
            .MaxAsync(ct)
            .ConfigureAwait(false);
    }
}
