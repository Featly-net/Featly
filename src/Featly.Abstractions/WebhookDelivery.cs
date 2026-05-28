namespace Featly;

/// <summary>
/// A single queued attempt to deliver a domain event to a
/// <see cref="WebhookEndpoint"/> (ARCHITECTURE.md §17). The delivery queue is
/// persisted so it survives a restart; a background worker claims rows whose
/// <see cref="NextAttemptAt"/> is due, POSTs the signed <see cref="Payload"/>,
/// and either marks them <see cref="WebhookDeliveryStatus.Succeeded"/> or
/// reschedules with exponential backoff until <see cref="AttemptCount"/>
/// exhausts the retry budget and the row goes <see cref="WebhookDeliveryStatus.Dead"/>.
/// </summary>
public sealed class WebhookDelivery
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>The endpoint this delivery targets.</summary>
    public required Guid WebhookEndpointId { get; init; }

    /// <summary>The event type that triggered the delivery (e.g. <c>flag.updated</c>).</summary>
    public required string EventType { get; init; }

    /// <summary>The exact JSON body that is POSTed and signed. Captured at enqueue time.</summary>
    public required string Payload { get; init; }

    /// <summary>Lifecycle status.</summary>
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

    /// <summary>Number of attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>When the next attempt is due. The worker claims rows at or past this time.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>HTTP status code of the most recent attempt, when one was made.</summary>
    public int? LastStatusCode { get; set; }

    /// <summary>Error detail from the most recent failed attempt.</summary>
    public string? LastError { get; set; }

    /// <summary>When the delivery first succeeded, if it did.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Lifecycle of a <see cref="WebhookDelivery"/>.</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Queued or awaiting the next retry (see <see cref="WebhookDelivery.NextAttemptAt"/>).</summary>
    Pending,

    /// <summary>Delivered successfully (endpoint returned a 2xx response).</summary>
    Succeeded,

    /// <summary>Retry budget exhausted; the delivery was abandoned (dead-letter).</summary>
    Dead,
}
