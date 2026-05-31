using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemorySystemSettingsStore : ISystemSettingsStore
{
    private readonly ConcurrentDictionary<string, SystemSetting> _settings = new(StringComparer.Ordinal);

    public Task<SystemSetting?> GetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_settings.TryGetValue(key, out var setting) ? setting : null);
    }

    public Task UpsertAsync(SystemSetting setting, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting.Key);
        _settings[setting.Key] = setting;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SystemSetting>> ListAsync(CancellationToken ct)
    {
        var list = _settings.Values
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<SystemSetting>>(list);
    }
}
