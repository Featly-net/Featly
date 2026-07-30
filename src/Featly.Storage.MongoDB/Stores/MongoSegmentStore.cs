using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoSegmentStore(MongoFeatlyDatabase database) : ISegmentStore
{
    public async Task<Segment?> GetAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.Segments
            .Find(s => s.EnvironmentId == environmentId && s.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Segment>> ListAsync(Guid environmentId, CancellationToken ct) =>
        await database.Segments
            .Find(s => s.EnvironmentId == environmentId && !s.Archived)
            .SortBy(s => s.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Segment>> ListArchivedAsync(Guid environmentId, CancellationToken ct) =>
        await database.Segments
            .Find(s => s.EnvironmentId == environmentId && s.Archived)
            .SortBy(s => s.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(Guid environmentId, Segment segment, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        segment.UpdatedAt = DateTimeOffset.UtcNow;
        segment.UpdatedBy = actor;

        var existing = await database.Segments
            .Find(s => s.EnvironmentId == environmentId && s.Key == segment.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.Segments.InsertOneAsync(segment, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        var replacement = new Segment
        {
            Id = existing.Id,
            Key = existing.Key,
            Name = segment.Name,
            Description = segment.Description,
            Conditions = segment.Conditions,
            EnvironmentId = existing.EnvironmentId,
            Archived = segment.Archived,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = segment.UpdatedAt,
            CreatedBy = existing.CreatedBy,
            UpdatedBy = segment.UpdatedBy,
        };

        await database.Segments.ReplaceOneAsync(s => s.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await database.Segments
            .DeleteOneAsync(s => s.EnvironmentId == environmentId && s.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var update = Builders<Segment>.Update
            .Set(s => s.Archived, true)
            .Set(s => s.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(s => s.UpdatedBy, actor);

        await database.Segments
            .UpdateOneAsync(s => s.EnvironmentId == environmentId && s.Key == key, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UnarchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var update = Builders<Segment>.Update
            .Set(s => s.Archived, false)
            .Set(s => s.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(s => s.UpdatedBy, actor);

        await database.Segments
            .UpdateOneAsync(s => s.EnvironmentId == environmentId && s.Key == key, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }
}
