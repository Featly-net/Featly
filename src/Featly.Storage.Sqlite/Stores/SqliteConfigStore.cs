using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteConfigStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IConfigStore
{
    public async Task<Config?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Configs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EnvironmentId == environmentId && c.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Config>> ListAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Configs.AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId && !c.Archived)
            .OrderBy(c => c.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(Guid environmentId, Config config, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        config.UpdatedAt = DateTimeOffset.UtcNow;
        config.UpdatedBy = actor;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Configs
            .Include(c => c.Rules)
            .FirstOrDefaultAsync(c => c.EnvironmentId == environmentId && c.Key == config.Key, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Configs.Add(config);
        }
        else
        {
            existing.Name = config.Name;
            existing.Description = config.Description;
            existing.Type = config.Type;
            existing.DefaultValue = config.DefaultValue;
            existing.Tags = config.Tags;
            existing.Archived = config.Archived;
            existing.UpdatedAt = config.UpdatedAt;
            existing.UpdatedBy = config.UpdatedBy;

            existing.Rules.Clear();
            foreach (var rule in config.Rules)
            {
                existing.Rules.Add(rule);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Configs
            .FirstOrDefaultAsync(c => c.EnvironmentId == environmentId && c.Key == key, ct)
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

        // SQLite ORDER BY on DateTimeOffset fails — aggregate client-side, same
        // pattern as SqliteFlagStore.GetMostRecentUpdateAsync.
        var timestamps = await db.Configs.AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId)
            .Select(c => c.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return timestamps.Count == 0 ? null : timestamps.Max();
    }
}
