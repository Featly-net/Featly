using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoSystemSettingsStore(MongoFeatlyDatabase database) : ISystemSettingsStore
{
    public async Task<SystemSetting?> GetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.SystemSettings
            .Find(s => s.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(SystemSetting setting, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting.Key);

        await database.SystemSettings.ReplaceOneAsync(
            s => s.Key == setting.Key,
            setting,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct) =>
        await database.SystemSettings
            .Find(FilterDefinition<SystemSetting>.Empty)
            .SortBy(s => s.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
