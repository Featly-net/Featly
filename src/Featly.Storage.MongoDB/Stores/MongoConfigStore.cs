using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoConfigStore(MongoFeatlyDatabase database) : IConfigStore
{
    public async Task<Config?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.Configs
            .Find(c => c.EnvironmentId == environmentId && c.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Config>> ListAsync(Guid environmentId, CancellationToken ct) =>
        await database.Configs
            .Find(c => c.EnvironmentId == environmentId && !c.Archived)
            .SortBy(c => c.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Config>> ListArchivedAsync(Guid environmentId, CancellationToken ct) =>
        await database.Configs
            .Find(c => c.EnvironmentId == environmentId && c.Archived)
            .SortBy(c => c.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(Guid environmentId, Config config, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        config.UpdatedAt = DateTimeOffset.UtcNow;
        config.UpdatedBy = actor;

        var existing = await database.Configs
            .Find(c => c.EnvironmentId == environmentId && c.Key == config.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.Configs.InsertOneAsync(config, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        var replacement = new Config
        {
            Id = existing.Id,
            Key = existing.Key,
            Name = config.Name,
            Description = config.Description,
            Type = config.Type,
            DefaultValue = config.DefaultValue,
            Rules = config.Rules,
            EnvironmentId = existing.EnvironmentId,
            Tags = config.Tags,
            Archived = config.Archived,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = config.UpdatedAt,
            CreatedBy = existing.CreatedBy,
            UpdatedBy = config.UpdatedBy,
        };

        await database.Configs.ReplaceOneAsync(c => c.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var update = Builders<Config>.Update
            .Set(c => c.Archived, true)
            .Set(c => c.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(c => c.UpdatedBy, actor);

        await database.Configs
            .UpdateOneAsync(c => c.EnvironmentId == environmentId && c.Key == key, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UnarchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var update = Builders<Config>.Update
            .Set(c => c.Archived, false)
            .Set(c => c.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(c => c.UpdatedBy, actor);

        await database.Configs
            .UpdateOneAsync(c => c.EnvironmentId == environmentId && c.Key == key, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }
}
