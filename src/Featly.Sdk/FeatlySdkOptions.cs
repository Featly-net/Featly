namespace Featly.Sdk;

/// <summary>
/// Configuration for the Featly SDK. Populated by <c>FeatlyClientBuilder</c>
/// or by binding the <c>Featly:Sdk</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class FeatlySdkOptions
{
    /// <summary>Configuration section name when bound from <c>appsettings.json</c>.</summary>
    public const string SectionName = "Featly:Sdk";

    /// <summary>Base URL of the Featly server, including scheme.</summary>
    public Uri? ServerUrl { get; set; }

    /// <summary>API key with <c>SdkRead</c> scope, sent as a bearer token.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Logical environment key (e.g. <c>"development"</c>, <c>"production"</c>).
    /// When empty, the server's default environment is used.
    /// </summary>
    public string? EnvironmentKey { get; set; }

    /// <summary>
    /// Interval between polling refreshes when the SSE stream is unavailable.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <c>true</c> (default), the SDK opens a long-lived SSE connection
    /// to <c>/api/sdk/stream</c> and uses polling only as a fallback.
    /// </summary>
    public bool EnableStreaming { get; set; } = true;

    /// <summary>
    /// HTTP request timeout for one-shot config fetches and pushed events.
    /// SSE connection uses its own per-event timeout.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
