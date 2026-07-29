using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoFlagStore(MongoFeatlyDatabase database) : IFlagStore
{
    public async Task<Flag?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.Flags
            .Find(f => f.EnvironmentId == environmentId && f.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Flag>> ListAsync(Guid environmentId, CancellationToken ct) =>
        await database.Flags
            .Find(f => f.EnvironmentId == environmentId && !f.Archived)
            .SortBy(f => f.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Flag>> ListArchivedAsync(Guid environmentId, CancellationToken ct) =>
        await database.Flags
            .Find(f => f.EnvironmentId == environmentId && f.Archived)
            .SortBy(f => f.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(Guid environmentId, Flag flag, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        flag.UpdatedAt = DateTimeOffset.UtcNow;
        flag.UpdatedBy = actor;

        var existing = await database.Flags
            .Find(f => f.EnvironmentId == environmentId && f.Key == flag.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.Flags.InsertOneAsync(flag, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        // Replace-in-place, keeping the original _id/CreatedAt/CreatedBy — same
        // "copy mutable fields" contract every other provider's UpsertAsync uses.
        var replacement = new Flag
        {
            Id = existing.Id,
            Key = existing.Key,
            Name = flag.Name,
            Description = flag.Description,
            Type = flag.Type,
            Enabled = flag.Enabled,
            DefaultVariantKey = flag.DefaultVariantKey,
            Variants = flag.Variants,
            Rules = flag.Rules,
            Prerequisites = flag.Prerequisites,
            EnvironmentId = existing.EnvironmentId,
            Tags = flag.Tags,
            Archived = flag.Archived,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = flag.UpdatedAt,
            CreatedBy = existing.CreatedBy,
            UpdatedBy = flag.UpdatedBy,
        };

        await database.Flags.ReplaceOneAsync(f => f.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var update = Builders<Flag>.Update
            .Set(f => f.Archived, true)
            .Set(f => f.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(f => f.UpdatedBy, actor);

        await database.Flags
            .UpdateOneAsync(f => f.EnvironmentId == environmentId && f.Key == key, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UnarchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var update = Builders<Flag>.Update
            .Set(f => f.Archived, false)
            .Set(f => f.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(f => f.UpdatedBy, actor);

        await database.Flags
            .UpdateOneAsync(f => f.EnvironmentId == environmentId && f.Key == key, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }
}
