using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.EntityFramework;

/// <summary>
/// Provider-agnostic <see cref="IWebhookStore"/> implemented over EF Core. The
/// relational providers (SQLite, Postgres) derive a one-line subclass bound to
/// their own <typeparamref name="TContext"/>. Compiled into each provider assembly
/// as a linked source file — ADR-0026 keeps the DbContext internal and per-provider,
/// so there is no shared assembly to host this; every query uses
/// <c>Set&lt;WebhookEndpoint&gt;()</c> so it stays context-agnostic.
/// </summary>
internal abstract class EfWebhookStore<TContext>(IDbContextFactory<TContext> contextFactory) : IWebhookStore
    where TContext : DbContext
{
    public async Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<WebhookEndpoint>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<WebhookEndpoint>().AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(WebhookEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<WebhookEndpoint>()
            .FirstOrDefaultAsync(e => e.Id == endpoint.Id, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Set<WebhookEndpoint>().Add(endpoint);
        }
        else
        {
            // Circuit-breaker fields are intentionally not copied here — they are
            // worker-managed (RecordCircuitStateAsync) and an admin edit must not
            // reset a tripped circuit.
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

    public async Task RecordCircuitStateAsync(Guid id, int consecutiveFailures, DateTimeOffset? circuitOpenUntil, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.Set<WebhookEndpoint>()
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.ConsecutiveFailures, consecutiveFailures)
                    .SetProperty(e => e.CircuitOpenUntil, circuitOpenUntil)
                    .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<WebhookEndpoint>().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        db.Set<WebhookEndpoint>().Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
