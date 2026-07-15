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

    /// <summary>Approval-defaults aggregate key (fallback policy templates).</summary>
    public const string ApprovalDefaults = "approval-defaults";

    /// <summary>Rate-limiting aggregate key (per-surface request throttling).</summary>
    public const string RateLimit = "rate-limit";

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

    /// <summary>
    /// Consecutive failures that trip an endpoint's circuit breaker (issue #207).
    /// A non-positive value disables the breaker.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>How long (seconds) a tripped circuit stays open before a half-open probe.</summary>
    public int CircuitBreakerCooldownSeconds { get; set; } = 300;
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

/// <summary>
/// DB-overridable default approval policy applied to an environment that has no
/// explicit <see cref="ApprovalPolicy"/> (ARCHITECTURE.md §15). Production-named
/// and non-production environments get separate templates. Defaults keep the
/// historical behavior — neither requires approval — so an operator opts into
/// stricter defaults rather than having them imposed.
/// </summary>
public sealed class FeatlyApprovalDefaultsSettings
{
    /// <summary>Configuration section name (<c>Featly:ApprovalDefaults</c>).</summary>
    public const string SectionName = "Featly:ApprovalDefaults";

    /// <summary>Template for environments whose key contains <c>prod</c>.</summary>
    public FeatlyApprovalPolicyTemplate Prod { get; set; } = new();

    /// <summary>Template for all other (non-production) environments.</summary>
    public FeatlyApprovalPolicyTemplate NonProd { get; set; } = new();

    /// <summary>Picks the template for an environment key (prod-named vs the rest).</summary>
    public FeatlyApprovalPolicyTemplate TemplateFor(string? environmentKey)
        => (environmentKey ?? string.Empty).Contains("prod", StringComparison.OrdinalIgnoreCase) ? Prod : NonProd;
}

/// <summary>
/// DB-overridable request rate limiting (SECURITY_AUDIT.md follow-up). Off by
/// default — an embedded host opts in. Limits are fixed windows of one minute,
/// partitioned per client (authenticated identity when present, else remote IP)
/// and per surface: the auth endpoints (brute-force protection), the admin API,
/// and the SDK API. Zero disables the limit for that surface.
/// </summary>
public sealed class FeatlyRateLimitSettings
{
    /// <summary>
    /// Master switch for the admin and SDK surfaces. Off by default so embedded
    /// hosts opt in. The auth surface's login POST is throttled regardless (see
    /// <see cref="AuthPermitsPerMinute"/>).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Requests per minute per client against <c>/api/auth/*</c> (login
    /// brute-force / Argon2-DoS guard). Applies to credential-submitting POSTs
    /// even when <see cref="Enabled"/> is off; read probes (GET <c>/me</c>) are
    /// throttled only when the master switch is on. 0 = unlimited.
    /// </summary>
    public int AuthPermitsPerMinute { get; set; } = 10;

    /// <summary>Requests per minute per client against <c>/api/admin/*</c>. 0 = unlimited.</summary>
    public int AdminPermitsPerMinute { get; set; } = 300;

    /// <summary>Requests per minute per client against <c>/api/sdk/*</c>. 0 = unlimited.</summary>
    public int SdkPermitsPerMinute { get; set; } = 1000;
}

/// <summary>A default approval-policy shape (no approver rules — those stay per-environment).</summary>
public sealed class FeatlyApprovalPolicyTemplate
{
    /// <summary>When <c>true</c>, mutations to a matching environment require approval.</summary>
    public bool Required { get; set; }

    /// <summary>Minimum approvals required.</summary>
    public int MinApprovals { get; set; } = 1;

    /// <summary>Whether the change author may approve their own change.</summary>
    public bool AuthorCanApproveOwnChange { get; set; }

    /// <summary>Whether an emergency bypass is allowed.</summary>
    public bool AllowEmergencyBypass { get; set; } = true;

    /// <summary>Materializes this template into an <see cref="ApprovalPolicy"/> for an environment.</summary>
    public ApprovalPolicy ToPolicy(Guid environmentId) => new()
    {
        Id = Guid.Empty,
        EnvironmentId = environmentId,
        Required = Required,
        MinApprovals = MinApprovals < 1 ? 1 : MinApprovals,
        AuthorCanApproveOwnChange = AuthorCanApproveOwnChange,
        AllowEmergencyBypass = AllowEmergencyBypass,
    };
}
