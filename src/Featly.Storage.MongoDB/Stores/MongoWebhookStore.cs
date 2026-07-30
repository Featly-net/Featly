using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoWebhookStore(MongoFeatlyDatabase database) : IWebhookStore
{
    public async Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.WebhookEndpoints
            .Find(e => e.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(CancellationToken ct) =>
        await database.WebhookEndpoints
            .Find(FilterDefinition<WebhookEndpoint>.Empty)
            .SortByDescending(e => e.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(WebhookEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await database.WebhookEndpoints
            .Find(e => e.Id == endpoint.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.WebhookEndpoints.InsertOneAsync(endpoint, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        // Circuit-breaker fields are intentionally not copied here — they are
        // worker-managed (RecordCircuitStateAsync) and an admin edit must not
        // reset a tripped circuit.
        var replacement = new WebhookEndpoint
        {
            Id = existing.Id,
            Name = endpoint.Name,
            Url = endpoint.Url,
            Secret = endpoint.Secret,
            Enabled = endpoint.Enabled,
            EventTypes = endpoint.EventTypes,
            EnvironmentId = endpoint.EnvironmentId,
            ConsecutiveFailures = existing.ConsecutiveFailures,
            CircuitOpenUntil = existing.CircuitOpenUntil,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt,
        };

        await database.WebhookEndpoints.ReplaceOneAsync(e => e.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task RecordCircuitStateAsync(Guid id, int consecutiveFailures, DateTimeOffset? circuitOpenUntil, CancellationToken ct)
    {
        var update = Builders<WebhookEndpoint>.Update
            .Set(e => e.ConsecutiveFailures, consecutiveFailures)
            .Set(e => e.CircuitOpenUntil, circuitOpenUntil)
            .Set(e => e.UpdatedAt, DateTimeOffset.UtcNow);

        await database.WebhookEndpoints.UpdateOneAsync(e => e.Id == id, update, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await database.WebhookEndpoints.DeleteOneAsync(e => e.Id == id, ct).ConfigureAwait(false);
    }
}
