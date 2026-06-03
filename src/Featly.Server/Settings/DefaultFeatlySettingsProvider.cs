using System.Text.Json;
using System.Text.Json.Serialization;
using Featly.Server.Authentication;
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
    private static readonly JsonSerializerOptions s_json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    private readonly StorageFacade _store;
    private readonly IOptionsMonitor<WebhookOptions> _webhookOptions;
    private readonly IOptionsMonitor<FeatlyAuthorizationOptions> _authzOptions;
    private readonly bool _webhookSectionExists;
    private readonly Lock _gate = new();

    private FeatlyWebhookSettings _webhook;
    private FeatlySettingsSource _webhookSource;
    private FeatlyAuthorizationSettings _authorization;
    private FeatlySettingsSource _authorizationSource;

    public DefaultFeatlySettingsProvider(
        StorageFacade store,
        IOptionsMonitor<WebhookOptions> webhookOptions,
        IOptionsMonitor<FeatlyAuthorizationOptions> authzOptions,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _store = store;
        _webhookOptions = webhookOptions;
        _authzOptions = authzOptions;
        _webhookSectionExists = configuration.GetSection(WebhookOptions.SectionName).Exists();
        (_webhook, _webhookSource) = WebhookFromAppSettings();
        (_authorization, _authorizationSource) = AuthorizationFromAppSettings();
    }

    public FeatlyWebhookSettings Webhook
    {
        get { lock (_gate) { return _webhook; } }
    }

    public FeatlySettingsSource WebhookSource
    {
        get { lock (_gate) { return _webhookSource; } }
    }

    public FeatlyAuthorizationSettings Authorization
    {
        get { lock (_gate) { return _authorization; } }
    }

    public FeatlySettingsSource AuthorizationSource
    {
        get { lock (_gate) { return _authorizationSource; } }
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var webhookDb = await _store.Settings.GetAsync(FeatlySettingsKeys.Webhook, ct).ConfigureAwait(false);
        var webhook = webhookDb is not null && webhookDb.Payload.Deserialize<FeatlyWebhookSettings>(s_json) is { } w
            ? (w, FeatlySettingsSource.Database)
            : WebhookFromAppSettings();

        var authzDb = await _store.Settings.GetAsync(FeatlySettingsKeys.Authorization, ct).ConfigureAwait(false);
        var authz = authzDb is not null && authzDb.Payload.Deserialize<FeatlyAuthorizationSettings>(s_json) is { } a
            ? (a, FeatlySettingsSource.Database)
            : AuthorizationFromAppSettings();

        lock (_gate)
        {
            _webhook = webhook.Item1;
            _webhookSource = webhook.Item2;
            _authorization = authz.Item1;
            _authorizationSource = authz.Item2;
        }
    }

    private (FeatlyAuthorizationSettings Value, FeatlySettingsSource Source) AuthorizationFromAppSettings()
    {
        var o = _authzOptions.CurrentValue;
        // AutoProvisionMode is nullable in options; an unset value falls back to
        // the hardcoded Open floor (matching the checker's historical default).
        var value = new FeatlyAuthorizationSettings
        {
            AutoProvisionMode = o.AutoProvisionMode ?? AutoProvisionMode.Open,
        };
        return (value, o.AutoProvisionMode.HasValue ? FeatlySettingsSource.AppSettings : FeatlySettingsSource.HardcodedDefault);
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
