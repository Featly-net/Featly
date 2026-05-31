using System.Text.Json;
using Featly.Server.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Settings;

/// <summary>
/// Default <see cref="IFeatlySettingsProvider"/>: merges the hardcoded default,
/// the <c>appsettings.json</c> value, and the database singleton (DB wins) and
/// caches the result. The cache is seeded from the appsettings layer in the
/// constructor so the provider is valid before the first <see cref="ReloadAsync"/>
/// (run at startup by <see cref="SettingsReloadHostedService"/>).
/// </summary>
internal sealed class DefaultFeatlySettingsProvider : IFeatlySettingsProvider
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly StorageFacade _store;
    private readonly IOptionsMonitor<WebhookOptions> _webhookOptions;
    private readonly bool _webhookSectionExists;
    private readonly Lock _gate = new();

    private FeatlyWebhookSettings _webhook;
    private FeatlySettingsSource _webhookSource;

    public DefaultFeatlySettingsProvider(
        StorageFacade store,
        IOptionsMonitor<WebhookOptions> webhookOptions,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _store = store;
        _webhookOptions = webhookOptions;
        _webhookSectionExists = configuration.GetSection(WebhookOptions.SectionName).Exists();
        (_webhook, _webhookSource) = WebhookFromAppSettings();
    }

    public FeatlyWebhookSettings Webhook
    {
        get { lock (_gate) { return _webhook; } }
    }

    public FeatlySettingsSource WebhookSource
    {
        get { lock (_gate) { return _webhookSource; } }
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var db = await _store.Settings.GetAsync(FeatlySettingsKeys.Webhook, ct).ConfigureAwait(false);

        FeatlyWebhookSettings value;
        FeatlySettingsSource source;
        if (db is not null && db.Payload.Deserialize<FeatlyWebhookSettings>(s_json) is { } fromDb)
        {
            value = fromDb;
            source = FeatlySettingsSource.Database;
        }
        else
        {
            (value, source) = WebhookFromAppSettings();
        }

        lock (_gate)
        {
            _webhook = value;
            _webhookSource = source;
        }
    }

    private (FeatlyWebhookSettings Value, FeatlySettingsSource Source) WebhookFromAppSettings()
    {
        var o = _webhookOptions.CurrentValue;
        var value = new FeatlyWebhookSettings
        {
            MaxAttempts = o.MaxAttempts,
            BaseRetryDelaySeconds = (int)o.BaseRetryDelay.TotalSeconds,
            MaxRetryDelaySeconds = (int)o.MaxRetryDelay.TotalSeconds,
        };
        return (value, _webhookSectionExists ? FeatlySettingsSource.AppSettings : FeatlySettingsSource.HardcodedDefault);
    }
}
