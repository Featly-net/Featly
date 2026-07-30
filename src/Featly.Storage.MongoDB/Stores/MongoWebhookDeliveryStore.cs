using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoWebhookDeliveryStore(MongoFeatlyDatabase database) : IWebhookDeliveryStore
{
    public async Task EnqueueAsync(IReadOnlyList<WebhookDelivery> deliveries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        if (deliveries.Count == 0)
        {
            return;
        }

        await database.WebhookDeliveries.InsertManyAsync(deliveries, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct) =>
        await database.WebhookDeliveries
            .Find(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt <= now)
            .SortBy(d => d.NextAttemptAt)
            .Limit(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<bool> TryClaimDueAsync(Guid id, DateTimeOffset dueBefore, DateTimeOffset leaseUntil, CancellationToken ct)
    {
        var update = Builders<WebhookDelivery>.Update
            .Set(d => d.NextAttemptAt, leaseUntil)
            .Set(d => d.UpdatedAt, DateTimeOffset.UtcNow);

        var result = await database.WebhookDeliveries
            .UpdateOneAsync(
                d => d.Id == id && d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt <= dueBefore,
                update,
                cancellationToken: ct)
            .ConfigureAwait(false);

        return result.ModifiedCount == 1;
    }

    public async Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        delivery.UpdatedAt = DateTimeOffset.UtcNow;

        var update = Builders<WebhookDelivery>.Update
            .Set(d => d.Status, delivery.Status)
            .Set(d => d.AttemptCount, delivery.AttemptCount)
            .Set(d => d.NextAttemptAt, delivery.NextAttemptAt)
            .Set(d => d.LastStatusCode, delivery.LastStatusCode)
            .Set(d => d.LastError, delivery.LastError)
            .Set(d => d.DeliveredAt, delivery.DeliveredAt)
            .Set(d => d.UpdatedAt, delivery.UpdatedAt);

        await database.WebhookDeliveries.UpdateOneAsync(d => d.Id == delivery.Id, update, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> ListByEndpointAsync(Guid webhookEndpointId, int max, CancellationToken ct) =>
        await database.WebhookDeliveries
            .Find(d => d.WebhookEndpointId == webhookEndpointId)
            .SortByDescending(d => d.CreatedAt)
            .Limit(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.WebhookDeliveries
            .Find(d => d.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
}
