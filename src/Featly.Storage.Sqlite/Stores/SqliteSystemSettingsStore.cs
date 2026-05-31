using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteSystemSettingsStore(IDbContextFactory<FeatlyDbContext> contextFactory) : ISystemSettingsStore
{
    public async Task<SystemSetting?> GetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(SystemSetting setting, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting.Key);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == setting.Key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            db.SystemSettings.Add(setting);
        }
        else
        {
            existing.Payload = setting.Payload;
            existing.UpdatedAt = setting.UpdatedAt;
            existing.UpdatedBy = setting.UpdatedBy;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.SystemSettings.AsNoTracking()
            .OrderBy(s => s.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
