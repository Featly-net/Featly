namespace Featly.Server.Telemetry;

/// <summary>
/// Configuration for Featly's server-side OpenTelemetry export pipeline. Bound
/// from the <c>Featly:Telemetry</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Observability is <b>off by default</b>. The instrumentation itself (the
/// Featly <see cref="System.Diagnostics.Metrics.Meter"/> and
/// <see cref="System.Diagnostics.ActivitySource"/>) is always present and costs
/// nothing while no collector is listening; what this section toggles is whether
/// <c>AddFeatlyServerTelemetry</c> wires the OpenTelemetry SDK (ASP.NET Core +
/// HttpClient instrumentation plus the OTLP exporter).
/// </para>
/// <para>
/// These settings are bootstrap-time infrastructure config and are therefore
/// <b>not</b> DB-overridable: the OpenTelemetry pipeline is built once at host
/// startup, before the database is reachable. This mirrors the
/// <c>OpenTelemetry.Endpoint</c> entry in ARCHITECTURE.md §15's config-only table.
/// </para>
/// </remarks>
public sealed class FeatlyTelemetryOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Featly:Telemetry";

    /// <summary>
    /// Master switch. When <c>false</c> (the default) <c>AddFeatlyServerTelemetry</c>
    /// is a no-op: no OpenTelemetry services are registered and there is no
    /// per-request instrumentation overhead.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When <c>true</c> (default) and <see cref="Enabled"/> is set, traces
    /// (ASP.NET Core + HttpClient spans plus Featly's custom spans) are exported.
    /// </summary>
    public bool Traces { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default) and <see cref="Enabled"/> is set, metrics
    /// (ASP.NET Core + HttpClient meters plus Featly's custom counters and
    /// histograms) are exported.
    /// </summary>
    public bool Metrics { get; set; } = true;

    /// <summary>
    /// Logical service name attached to every exported span and metric as the
    /// OpenTelemetry <c>service.name</c> resource attribute. Defaults to
    /// <c>"Featly"</c>.
    /// </summary>
    public string ServiceName { get; set; } = "Featly";

    /// <summary>
    /// OTLP collector endpoint (for example <c>http://localhost:4317</c> for gRPC
    /// or <c>http://localhost:4318</c> for HTTP/protobuf). When left <c>null</c>,
    /// the exporter falls back to the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
    /// environment variable and the OpenTelemetry default endpoint.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// OTLP wire protocol. <c>Grpc</c> (default) targets port 4317;
    /// <c>HttpProtobuf</c> targets port 4318.
    /// </summary>
    public FeatlyOtlpProtocol OtlpProtocol { get; set; } = FeatlyOtlpProtocol.Grpc;
}

/// <summary>OTLP exporter wire protocols supported by Featly.</summary>
public enum FeatlyOtlpProtocol
{
    /// <summary>gRPC transport over HTTP/2 (OTLP default port 4317).</summary>
    Grpc,

    /// <summary>protobuf payloads over HTTP/1.1 (OTLP default port 4318).</summary>
    HttpProtobuf,
}
