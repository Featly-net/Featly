namespace Featly.Server;

/// <summary>
/// Configuration for the Featly server. Bound from the
/// <c>Featly:Server</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// M2 uses static bearer tokens loaded from configuration. Real per-environment
/// API keys with Argon2 hashing land in M6.
/// </remarks>
public sealed class FeatlyServerOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Featly:Server";

    /// <summary>
    /// Bearer token required by every <c>/api/admin/*</c> endpoint.
    /// Must be set in production. Left empty in development to ease local exploration.
    /// </summary>
    public string AdminApiKey { get; set; } = "";

    /// <summary>
    /// Bearer token required by every <c>/api/sdk/*</c> endpoint.
    /// Must be set in production. Left empty in development to ease local exploration.
    /// </summary>
    public string SdkApiKey { get; set; } = "";

    /// <summary>
    /// When <c>true</c> (default), the server creates a default
    /// <see cref="Project"/> and <see cref="Environment"/> on first boot if
    /// none exist. Honors the Hangfire-style zero-friction quickstart.
    /// </summary>
    public bool AutoCreateDefaultProject { get; set; } = true;

    /// <summary>
    /// Key used for the auto-created default project. Defaults to <c>"default"</c>.
    /// </summary>
    public string DefaultProjectKey { get; set; } = "default";

    /// <summary>
    /// Key used for the auto-created default environment. Defaults to
    /// <c>"development"</c> (the typical embedded local-dev value). Production
    /// hosts can override via configuration.
    /// </summary>
    public string DefaultEnvironmentKey { get; set; } = "development";

    /// <summary>
    /// Opt-out toggles for the server's feature areas (ADR-0024). Every area is
    /// enabled by default; disable one to drop its admin endpoint group and hide
    /// its dashboard UI (for example a flags-only or configs-only deployment).
    /// </summary>
    public FeatlyFeatureOptions Features { get; set; } = new();

    /// <summary>
    /// Maximum number of events accepted in a single <c>POST /api/sdk/events</c>
    /// batch (issue #204). The SDK batches at 200; this server-side cap stops a
    /// compromised SDK key from flooding the store with an oversized batch.
    /// A non-positive value disables the cap.
    /// </summary>
    public int MaxEventBatchSize { get; set; } = 1000;
}
