using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoApiKeyStore(MongoFeatlyDatabase database) : IApiKeyStore
{
    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.ApiKeys
            .Find(k => k.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ApiKey>> FindCandidatesByPrefixAsync(string prefix, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return await database.ApiKeys
            .Find(k => !k.Revoked && k.Prefix == prefix)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiKey>> ListAsync(Guid environmentId, CancellationToken ct) =>
        await database.ApiKeys
            .Find(k => k.EnvironmentId == environmentId)
            .SortByDescending(k => k.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task CreateAsync(ApiKey apiKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        await database.ApiKeys.InsertOneAsync(apiKey, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task RevokeAsync(Guid id, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var update = Builders<ApiKey>.Update.Set(k => k.Revoked, true);
        await database.ApiKeys.UpdateOneAsync(k => k.Id == id, update, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task TouchLastUsedAsync(Guid id, DateTimeOffset at, CancellationToken ct)
    {
        var update = Builders<ApiKey>.Update.Set(k => k.LastUsedAt, at);
        await database.ApiKeys.UpdateOneAsync(k => k.Id == id, update, cancellationToken: ct).ConfigureAwait(false);
    }
}
