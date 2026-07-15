using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.EntityFramework;

/// <summary>
/// Provider-agnostic <see cref="IWebhookDeliveryStore"/> implemented over EF Core.
/// The relational providers (SQLite, Postgres) derive a one-line subclass bound to
/// their own <typeparamref name="TContext"/>. Compiled into each provider assembly
/// as a linked source file — ADR-0026 keeps the DbContext internal and per-provider,
/// so there is no shared assembly to host this; every query uses
/// <c>Set&lt;WebhookDelivery&gt;()</c> so it stays context-agnostic.
/// </summary>
internal abstract class EfWebhookDeliveryStore<TContext>(IDbContextFactory<TContext> contextFactory) : IWebhookDeliveryStore
    where TContext : DbContext
{
    public async Task EnqueueAsync(IReadOnlyList<WebhookDelivery> deliveries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        if (deliveries.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.Set<WebhookDelivery>().AddRange(deliveries);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(DateTimeOffset now, int max, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<WebhookDelivery>().AsNoTracking()
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryClaimDueAsync(Guid id, DateTimeOffset dueBefore, DateTimeOffset leaseUntil, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var affected = await db.Set<WebhookDelivery>()
            .Where(d => d.Id == id
                && d.Status == WebhookDeliveryStatus.Pending
                && d.NextAttemptAt <= dueBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.NextAttemptAt, leaseUntil)
                    .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false);
        return affected == 1;
    }

    public async Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        delivery.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<WebhookDelivery>()
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
        return await db.Set<WebhookDelivery>().AsNoTracking()
            .Where(d => d.WebhookEndpointId == webhookEndpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<WebhookDelivery>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);
    }
}
