using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoEnvironmentStore(MongoFeatlyDatabase database) : IEnvironmentStore
{
    public async Task<Environment?> GetByKeyAsync(Guid projectId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.Environments
            .Find(e => e.ProjectId == projectId && e.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Environment?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.Environments
            .Find(e => e.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<Environment?> GetDefaultAsync(Guid projectId, CancellationToken ct) =>
        await database.Environments
            .Find(e => e.ProjectId == projectId && e.IsDefault)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Environment>> ListAsync(Guid projectId, CancellationToken ct) =>
        await database.Environments
            .Find(e => e.ProjectId == projectId)
            .SortBy(e => e.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task CreateAsync(Environment environment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var keyTaken = await database.Environments
            .Find(e => e.ProjectId == environment.ProjectId && e.Key == environment.Key)
            .AnyAsync(ct)
            .ConfigureAwait(false);
        if (keyTaken)
        {
            throw new InvalidOperationException(
                $"An environment with key '{environment.Key}' already exists in project '{environment.ProjectId}'.");
        }

        try
        {
            await database.Environments.InsertOneAsync(environment, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException(
                $"An environment with key '{environment.Key}' already exists in project '{environment.ProjectId}'.", ex);
        }
    }

    public async Task<Environment?> SetReadOnlyAsync(Guid id, bool readOnly, CancellationToken ct)
    {
        var update = Builders<Environment>.Update.Set(e => e.ReadOnly, readOnly);
        return await database.Environments
            .FindOneAndUpdateAsync<Environment>(
                e => e.Id == id,
                update,
                new FindOneAndUpdateOptions<Environment> { ReturnDocument = ReturnDocument.After },
                ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(Environment environment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var update = Builders<Environment>.Update.Set(e => e.Name, environment.Name);
        var result = await database.Environments
            .UpdateOneAsync(e => e.Id == environment.Id, update, cancellationToken: ct)
            .ConfigureAwait(false);

        if (result.MatchedCount == 0)
        {
            throw new InvalidOperationException($"Environment '{environment.Key}' not found.");
        }
    }

    public async Task BumpConfigVersionAsync(Guid id, CancellationToken ct)
    {
        // A single atomic $inc, so two concurrent writers cannot read-modify-
        // write over each other and lose a bump — which would leave SDK
        // clients on a stale snapshot (issue #228).
        var update = Builders<Environment>.Update.Inc(e => e.ConfigVersion, 1L);
        await database.Environments
            .UpdateOneAsync(e => e.Id == id, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await database.Environments
            .DeleteOneAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);
    }
}
