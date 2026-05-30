namespace Featly.Server.Telemetry;

/// <summary>
/// Well-known names for Featly's server-side OpenTelemetry signals. Consumers
/// that run their own OpenTelemetry pipeline (rather than letting Featly wire
/// the OTLP exporter via <c>AddFeatlyServerTelemetry</c>) can subscribe to
/// Featly's traces and metrics by adding these names to their
/// <c>TracerProviderBuilder.AddSource(...)</c> and
/// <c>MeterProviderBuilder.AddMeter(...)</c> calls.
/// </summary>
public static class FeatlyTelemetry
{
    /// <summary>
    /// Name of the <see cref="System.Diagnostics.ActivitySource"/> that emits
    /// Featly's server-side spans (for example <c>featly.change.apply</c> and
    /// <c>featly.webhook.deliver</c>).
    /// </summary>
    public const string ActivitySourceName = "Featly.Server";

    /// <summary>
    /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> that publishes
    /// Featly's server-side metrics (evaluations, event ingestion, applied
    /// changes, audit writes, and webhook deliveries).
    /// </summary>
    public const string MeterName = "Featly.Server";
}
