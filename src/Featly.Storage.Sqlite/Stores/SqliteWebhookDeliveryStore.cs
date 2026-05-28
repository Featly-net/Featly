using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteWebhookDeliveryStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IWebhookDeliveryStore
{
    public async Task EnqueueAsync(IReadOnlyList<WebhookDelivery> deliveries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        if (deliveries.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.WebhookDeliveries.AddRange(deliveries);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        delivery.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.WebhookDeliveries
            .FirstOrDefaultAsync(d => d.Id == delivery.Id, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        existing.Status = delivery.Status;
        existing.AttemptCount = delivery.AttemptCount;
        existing.NextAttemptAt = delivery.NextAttemptAt;
        existing.LastStatusCode = delivery.LastStatusCode;
        existing.LastError = delivery.LastError;
        existing.DeliveredAt = delivery.DeliveredAt;
        existing.UpdatedAt = delivery.UpdatedAt;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> ListByEndpointAsync(Guid webhookEndpointId, int max, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.WebhookEndpointId == webhookEndpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.WebhookDeliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);
    }
}
