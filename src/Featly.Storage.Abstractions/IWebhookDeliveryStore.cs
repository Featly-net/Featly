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

    /// <summary>Persists the outcome of an attempt (status, attempt count, next time, error).</summary>
    Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct);

    /// <summary>Lists recent deliveries for an endpoint, newest first — for the dashboard.</summary>
    Task<IReadOnlyList<WebhookDelivery>> ListByEndpointAsync(Guid webhookEndpointId, int max, CancellationToken ct);

    /// <summary>Returns the delivery with the given id, or <c>null</c>.</summary>
    Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct);
}
