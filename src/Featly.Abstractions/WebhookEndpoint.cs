namespace Featly;

/// <summary>
/// A registered outbound webhook (ARCHITECTURE.md §17). When a domain event the
/// endpoint subscribes to fires, the server enqueues a <see cref="WebhookDelivery"/>
/// targeting <see cref="Url"/> and signs the body with <see cref="Secret"/> using
/// HMAC-SHA256.
/// </summary>
public sealed class WebhookEndpoint
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Human-readable name shown in the dashboard.</summary>
    public required string Name { get; set; }

    /// <summary>Absolute URL events are POSTed to.</summary>
    public required string Url { get; set; }

    /// <summary>
    /// Shared secret used to sign each delivery body (HMAC-SHA256, sent in the
    /// <c>X-Featly-Signature</c> header). Treated as sensitive — never returned
    /// to the SDK and only echoed to admins that can manage webhooks.
    /// </summary>
    public string Secret { get; set; } = "";

    /// <summary>Whether deliveries are attempted. Disabled endpoints are skipped.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Event types this endpoint subscribes to (e.g. <c>flag.updated</c>). An
    /// empty list means "all events". See <see cref="FeatlyEventTypes"/>.
    /// </summary>
    public List<string> EventTypes { get; set; } = [];

    /// <summary>
    /// Optional environment filter. When set, only events scoped to this
    /// environment are delivered; <c>null</c> means events from any environment.
    /// </summary>
    public Guid? EnvironmentId { get; set; }

    /// <summary>
    /// Consecutive failed delivery attempts since the last success (issue #207).
    /// Drives the per-endpoint circuit breaker: once it reaches the configured
    /// threshold the circuit opens (see <see cref="CircuitOpenUntil"/>). Reset to
    /// zero on the next successful delivery. Managed by the delivery worker, not
    /// editable through the admin API.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// When set and in the future, the circuit is <b>open</b>: the delivery worker
    /// short-circuits this endpoint's due deliveries (reschedules them past this
    /// time without POSTing) so a consistently-failing endpoint cannot clog the
    /// queue (issue #207). After it elapses the next delivery is a half-open probe;
    /// success closes the circuit, failure re-opens it. <c>null</c> means closed.
    /// </summary>
    public DateTimeOffset? CircuitOpenUntil { get; set; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
