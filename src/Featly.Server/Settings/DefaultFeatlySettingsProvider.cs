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
    private readonly IOptionsMonitor<FeatlyAuditOptions> _auditOptions;
    private readonly IOptionsMonitor<FeatlyApprovalDefaultsSettings> _apprDefaultsOptions;
    private readonly IOptionsMonitor<RateLimiting.FeatlyRateLimitOptions> _rateLimitOptions;
    private readonly bool _webhookSectionExists;
    private readonly bool _auditSectionExists;
    private readonly bool _apprDefaultsSectionExists;
    private readonly bool _rateLimitSectionExists;
    private readonly Lock _gate = new();

    private FeatlyWebhookSettings _webhook;
    private FeatlySettingsSource _webhookSource;
    private FeatlyAuthorizationSettings _authorization;
    private FeatlySettingsSource _authorizationSource;
    private FeatlyAuditSettings _audit;
    private FeatlySettingsSource _auditSource;
    private FeatlyApprovalDefaultsSettings _approvalDefaults;
    private FeatlySettingsSource _approvalDefaultsSource;
    private FeatlyRateLimitSettings _rateLimit;
    private FeatlySettingsSource _rateLimitSource;

    public DefaultFeatlySettingsProvider(
        StorageFacade store,
        IOptionsMonitor<WebhookOptions> webhookOptions,
        IOptionsMonitor<FeatlyAuthorizationOptions> authzOptions,
        IOptionsMonitor<FeatlyAuditOptions> auditOptions,
        IOptionsMonitor<FeatlyApprovalDefaultsSettings> approvalDefaultsOptions,
        IOptionsMonitor<RateLimiting.FeatlyRateLimitOptions> rateLimitOptions,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _store = store;
        _webhookOptions = webhookOptions;
        _authzOptions = authzOptions;
        _auditOptions = auditOptions;
        _apprDefaultsOptions = approvalDefaultsOptions;
        _rateLimitOptions = rateLimitOptions;
        _webhookSectionExists = configuration.GetSection(WebhookOptions.SectionName).Exists();
        _auditSectionExists = configuration.GetSection(FeatlyAuditOptions.SectionName).Exists();
        _apprDefaultsSectionExists = configuration.GetSection(FeatlyApprovalDefaultsSettings.SectionName).Exists();
        _rateLimitSectionExists = configuration.GetSection(RateLimiting.FeatlyRateLimitOptions.SectionName).Exists();
        (_webhook, _webhookSource) = WebhookFromAppSettings();
        (_authorization, _authorizationSource) = AuthorizationFromAppSettings();
        (_audit, _auditSource) = AuditFromAppSettings();
        (_approvalDefaults, _approvalDefaultsSource) = ApprovalDefaultsFromAppSettings();
        (_rateLimit, _rateLimitSource) = RateLimitFromAppSettings();
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

    public FeatlyAuditSettings Audit
    {
        get { lock (_gate) { return _audit; } }
    }

    public FeatlySettingsSource AuditSource
    {
        get { lock (_gate) { return _auditSource; } }
    }

    public FeatlyApprovalDefaultsSettings ApprovalDefaults
    {
        get { lock (_gate) { return _approvalDefaults; } }
    }

    public FeatlySettingsSource ApprovalDefaultsSource
    {
        get { lock (_gate) { return _approvalDefaultsSource; } }
    }

    public FeatlyRateLimitSettings RateLimit
    {
        get { lock (_gate) { return _rateLimit; } }
    }

    public FeatlySettingsSource RateLimitSource
    {
        get { lock (_gate) { return _rateLimitSource; } }
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

        var auditDb = await _store.Settings.GetAsync(FeatlySettingsKeys.Audit, ct).ConfigureAwait(false);
        var audit = auditDb is not null && auditDb.Payload.Deserialize<FeatlyAuditSettings>(s_json) is { } au
            ? (au, FeatlySettingsSource.Database)
            : AuditFromAppSettings();

        var apprDb = await _store.Settings.GetAsync(FeatlySettingsKeys.ApprovalDefaults, ct).ConfigureAwait(false);
        var appr = apprDb is not null && apprDb.Payload.Deserialize<FeatlyApprovalDefaultsSettings>(s_json) is { } ad
            ? (ad, FeatlySettingsSource.Database)
            : ApprovalDefaultsFromAppSettings();

        var rateDb = await _store.Settings.GetAsync(FeatlySettingsKeys.RateLimit, ct).ConfigureAwait(false);
        var rate = rateDb is not null && rateDb.Payload.Deserialize<FeatlyRateLimitSettings>(s_json) is { } r
            ? (r, FeatlySettingsSource.Database)
            : RateLimitFromAppSettings();

        lock (_gate)
        {
            _webhook = webhook.Item1;
            _webhookSource = webhook.Item2;
            _authorization = authz.Item1;
            _authorizationSource = authz.Item2;
            _audit = audit.Item1;
            _auditSource = audit.Item2;
            _approvalDefaults = appr.Item1;
            _approvalDefaultsSource = appr.Item2;
            _rateLimit = rate.Item1;
            _rateLimitSource = rate.Item2;
        }
    }

    private (FeatlyAuditSettings Value, FeatlySettingsSource Source) AuditFromAppSettings()
    {
        var value = new FeatlyAuditSettings { RetentionDays = _auditOptions.CurrentValue.RetentionDays };
        return (value, _auditSectionExists ? FeatlySettingsSource.AppSettings : FeatlySettingsSource.HardcodedDefault);
    }

    private (FeatlyApprovalDefaultsSettings Value, FeatlySettingsSource Source) ApprovalDefaultsFromAppSettings()
        => (_apprDefaultsOptions.CurrentValue, _apprDefaultsSectionExists ? FeatlySettingsSource.AppSettings : FeatlySettingsSource.HardcodedDefault);

    private (FeatlyRateLimitSettings Value, FeatlySettingsSource Source) RateLimitFromAppSettings()
    {
        var o = _rateLimitOptions.CurrentValue;
        var value = new FeatlyRateLimitSettings
        {
            Enabled = o.Enabled,
            AuthPermitsPerMinute = o.AuthPermitsPerMinute,
            AdminPermitsPerMinute = o.AdminPermitsPerMinute,
            SdkPermitsPerMinute = o.SdkPermitsPerMinute,
        };
        return (value, _rateLimitSectionExists ? FeatlySettingsSource.AppSettings : FeatlySettingsSource.HardcodedDefault);
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
            CircuitBreakerThreshold = o.CircuitBreakerThreshold,
            CircuitBreakerCooldownSeconds = (int)o.CircuitBreakerCooldown.TotalSeconds,
        };
        return (value, _webhookSectionExists ? FeatlySettingsSource.AppSettings : FeatlySettingsSource.HardcodedDefault);
    }
}
