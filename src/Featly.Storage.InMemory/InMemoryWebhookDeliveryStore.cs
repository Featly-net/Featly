using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryWebhookDeliveryStore : IWebhookDeliveryStore
{
    private readonly ConcurrentDictionary<Guid, WebhookDelivery> _byId = new();

    public Task EnqueueAsync(IReadOnlyList<WebhookDelivery> deliveries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        foreach (var d in deliveries)
        {
            _byId[d.Id] = d;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct)
    {
        var list = _byId.Values
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(max)
            .ToList();
        return Task.FromResult<IReadOnlyList<WebhookDelivery>>(list);
    }

    public Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        delivery.UpdatedAt = DateTimeOffset.UtcNow;
        _byId[delivery.Id] = delivery;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WebhookDelivery>> ListByEndpointAsync(Guid webhookEndpointId, int max, CancellationToken ct)
    {
        var list = _byId.Values
            .Where(d => d.WebhookEndpointId == webhookEndpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(max)
            .ToList();
        return Task.FromResult<IReadOnlyList<WebhookDelivery>>(list);
    }

    public Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var d) ? d : null);
}
