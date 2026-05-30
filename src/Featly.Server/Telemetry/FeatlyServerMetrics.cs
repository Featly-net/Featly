using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Featly.Server.Telemetry;

/// <summary>
/// Owns Featly's server-side OpenTelemetry instruments — the
/// <see cref="System.Diagnostics.Metrics.Meter"/> and its counters/histograms,
/// plus the <see cref="System.Diagnostics.ActivitySource"/> for custom spans.
/// </summary>
/// <remarks>
/// Registered as a singleton by <c>AddFeatlyServer</c> and injected into the
/// services that produce telemetry. Recording is always safe: when no
/// OpenTelemetry pipeline (or other listener) is subscribed, every
/// <c>Record*</c> call and <c>ActivitySource.StartActivity</c>
/// short-circuits to a near-free no-op, so there is no measurable overhead when
/// observability is disabled.
/// </remarks>
internal sealed class FeatlyServerMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _evaluations;
    private readonly Counter<long> _eventsIngested;
    private readonly Counter<long> _changesApplied;
    private readonly Counter<long> _auditWrites;
    private readonly Counter<long> _webhookDeliveries;
    private readonly Histogram<double> _webhookDeliveryDuration;

    public FeatlyServerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(FeatlyTelemetry.MeterName);
        ActivitySource = new ActivitySource(FeatlyTelemetry.ActivitySourceName);

        _evaluations = _meter.CreateCounter<long>(
            "featly.server.evaluations",
            unit: "{evaluation}",
            description: "Server-side flag/config evaluations (the dashboard 'test this context' preview).");

        _eventsIngested = _meter.CreateCounter<long>(
            "featly.server.events_ingested",
            unit: "{event}",
            description: "Exposure and custom events accepted from the SDK.");

        _changesApplied = _meter.CreateCounter<long>(
            "featly.server.changes_applied",
            unit: "{change}",
            description: "Approved or emergency-bypassed pending changes applied to their entity.");

        _auditWrites = _meter.CreateCounter<long>(
            "featly.server.audit_writes",
            unit: "{entry}",
            description: "Audit-log entries appended from domain events.");

        _webhookDeliveries = _meter.CreateCounter<long>(
            "featly.server.webhook_deliveries",
            unit: "{delivery}",
            description: "Webhook delivery attempts, tagged by success or failure.");

        _webhookDeliveryDuration = _meter.CreateHistogram<double>(
            "featly.server.webhook_delivery_duration",
            unit: "ms",
            description: "Latency of a single outbound webhook delivery attempt.");
    }

    /// <summary>The source for Featly's custom server-side spans.</summary>
    public ActivitySource ActivitySource { get; }

    /// <summary>Records one server-side evaluation by entity type and outcome reason.</summary>
    public void RecordEvaluation(string entityType, string reason)
    {
        if (!_evaluations.Enabled)
        {
            return;
        }

        _evaluations.Add(1,
            new KeyValuePair<string, object?>("featly.entity_type", entityType),
            new KeyValuePair<string, object?>("featly.reason", reason));
    }

    /// <summary>Records <paramref name="count"/> ingested events of a single type.</summary>
    public void RecordEventsIngested(EventType type, long count)
    {
        if (count <= 0 || !_eventsIngested.Enabled)
        {
            return;
        }

        _eventsIngested.Add(count, new KeyValuePair<string, object?>("featly.event_type", type.ToString()));
    }

    /// <summary>Records one applied pending change by action and bypass flag.</summary>
    public void RecordChangeApplied(ChangeAction action, bool bypassed)
    {
        if (!_changesApplied.Enabled)
        {
            return;
        }

        _changesApplied.Add(1,
            new KeyValuePair<string, object?>("featly.change_action", action.ToString()),
            new KeyValuePair<string, object?>("featly.bypassed", bypassed));
    }

    /// <summary>Records one audit-log write, tagged by the domain action.</summary>
    public void RecordAuditWrite(string action)
    {
        if (!_auditWrites.Enabled)
        {
            return;
        }

        _auditWrites.Add(1, new KeyValuePair<string, object?>("featly.action", action));
    }

    /// <summary>Records the outcome and latency of a single webhook delivery attempt.</summary>
    public void RecordWebhookDelivery(bool success, double durationMs)
    {
        var result = new KeyValuePair<string, object?>("featly.result", success ? "success" : "failure");

        if (_webhookDeliveries.Enabled)
        {
            _webhookDeliveries.Add(1, result);
        }

        if (_webhookDeliveryDuration.Enabled)
        {
            _webhookDeliveryDuration.Record(durationMs, result);
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
        ActivitySource.Dispose();
    }
}
