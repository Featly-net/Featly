using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresWebhookStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IWebhookStore
{
    public async Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.WebhookEndpoints.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.WebhookEndpoints.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(WebhookEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpoint.Id, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.WebhookEndpoints.Add(endpoint);
        }
        else
        {
            existing.Name = endpoint.Name;
            existing.Url = endpoint.Url;
            existing.Secret = endpoint.Secret;
            existing.Enabled = endpoint.Enabled;
            existing.EventTypes = [.. endpoint.EventTypes];
            existing.EnvironmentId = endpoint.EnvironmentId;
            existing.UpdatedAt = endpoint.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.WebhookEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        db.WebhookEndpoints.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
