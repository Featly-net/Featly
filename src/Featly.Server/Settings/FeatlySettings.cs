namespace Featly.Server.Settings;

/// <summary>Stable keys for the DB-overridable settings singletons (ARCHITECTURE.md §15).</summary>
public static class FeatlySettingsKeys
{
    /// <summary>Webhook retry-tuning aggregate key.</summary>
    public const string Webhook = "webhook";

    /// <summary>Authorization aggregate key (auto-provision policy).</summary>
    public const string Authorization = "authorization";

    /// <summary>Audit aggregate key (log retention).</summary>
    public const string Audit = "audit";

    /// <summary>
    /// Entity type used on the <c>IChangeNotifier</c> notification emitted when a
    /// settings singleton changes, so other instances reload.
    /// </summary>
    public const string ChangeEntityType = "Settings";
}

/// <summary>Which precedence layer supplied an effective settings value.</summary>
public enum FeatlySettingsSource
{
    /// <summary>The in-code default (no <c>appsettings</c> section, no DB row).</summary>
    HardcodedDefault,

    /// <summary>The <c>appsettings.json</c> value (no DB override present).</summary>
    AppSettings,

    /// <summary>The database singleton (overrides <c>appsettings</c>).</summary>
    Database,
}

/// <summary>
/// DB-overridable webhook retry tuning (ARCHITECTURE.md §15). The bootstrap
/// operational knobs (<c>PollInterval</c>, <c>BatchSize</c>, <c>RequestTimeout</c>)
/// stay <c>appsettings</c>-only on <c>WebhookOptions</c>; only the retry cadence
/// is runtime-editable here. Defaults match <c>WebhookOptions</c>.
/// </summary>
public sealed class FeatlyWebhookSettings
{
    /// <summary>Total attempts before a delivery is dead-lettered.</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Base delay (seconds) for exponential backoff: base * 2^(attempt-1), capped.</summary>
    public int BaseRetryDelaySeconds { get; set; } = 10;

    /// <summary>Upper bound (seconds) on a single retry delay.</summary>
    public int MaxRetryDelaySeconds { get; set; } = 1800;
}

/// <summary>
/// DB-overridable authorization policy (ARCHITECTURE.md §15). For now this is the
/// auto-provision mode — what happens when an authenticated identifier has no
/// matching user. The bootstrap-admin identifier stays bootstrap-only (it is
/// consumed at startup), and default-role/default-project land in later slices.
/// </summary>
public sealed class FeatlyAuthorizationSettings
{
    /// <summary>
    /// What to do for an authenticated identifier with no role assignment:
    /// <c>Open</c> grants the Viewer floor, <c>Closed</c> denies.
    /// </summary>
    public Featly.Server.Authentication.AutoProvisionMode AutoProvisionMode { get; set; }
        = Featly.Server.Authentication.AutoProvisionMode.Open;
}

/// <summary>
/// DB-overridable audit-log retention (ARCHITECTURE.md §15). When
/// <see cref="RetentionDays"/> is greater than zero, a background trimmer deletes
/// audit entries older than that many days. Zero (the default) keeps the log
/// forever — preserving the historical behavior.
/// </summary>
public sealed class FeatlyAuditSettings
{
    /// <summary>Days of audit history to keep; <c>0</c> disables pruning (keep forever).</summary>
    public int RetentionDays { get; set; }
}
