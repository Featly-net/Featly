namespace Featly.Storage;

/// <summary>
/// The persisted webhook delivery queue. Survives restarts: the background
/// worker claims due rows, attempts the POST, and writes the result back.
/// </summary>
public interface IWebhookDeliveryStore
{
    /// <summary>Enqueues a batch of deliveries (one per matching endpoint).</summary>
    Task EnqueueAsync(IReadOnlyList<WebhookDelivery> deliveries, CancellationToken ct);

    /// <summary>
    /// Returns up to <paramref name="max"/> <see cref="WebhookDeliveryStatus.Pending"/>
    /// deliveries whose <see cref="WebhookDelivery.NextAttemptAt"/> is at or before
    /// <paramref name="now"/>, oldest first — the worker's claim query.
    /// </summary>
    Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct);

    /// <summary>
    /// Atomically leases a due delivery for one worker: if the row is still
    /// <see cref="WebhookDeliveryStatus.Pending"/> and due at or before
    /// <paramref name="dueBefore"/>, its <see cref="WebhookDelivery.NextAttemptAt"/>
    /// is pushed forward to <paramref name="leaseUntil"/> and the call returns
    /// <c>true</c>. A second worker draining the same queue then sees the row as
    /// not-yet-due and skips it, so several instances deliver each event once
    /// (issue #237). The attempt's <see cref="UpdateAsync"/> overwrites the lease
    /// with the real outcome; if the worker crashes, the lease expires and the row
    /// becomes due again (at-least-once, receiver deduplicates on delivery id).
    /// </summary>
    Task<bool> TryClaimDueAsync(Guid id, DateTimeOffset dueBefore, DateTimeOffset leaseUntil, CancellationToken ct);

    /// <summary>Persists the outcome of an attempt (status, attempt count, next time, error).</summary>
    Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct);

    /// <summary>Lists recent deliveries for an endpoint, newest first — for the dashboard.</summary>
    Task<IReadOnlyList<WebhookDelivery>> ListByEndpointAsync(Guid webhookEndpointId, int max, CancellationToken ct);

    /// <summary>Returns the delivery with the given id, or <c>null</c>.</summary>
    Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct);
}
