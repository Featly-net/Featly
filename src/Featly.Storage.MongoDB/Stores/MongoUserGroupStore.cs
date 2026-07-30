using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoUserGroupStore(MongoFeatlyDatabase database) : IUserGroupStore
{
    public async Task<UserGroup?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.UserGroups
            .Find(g => g.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<UserGroup?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.UserGroups
            .Find(g => g.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<UserGroup>> ListAsync(CancellationToken ct) =>
        await database.UserGroups
            .Find(FilterDefinition<UserGroup>.Empty)
            .SortBy(g => g.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<UserGroup>> ListForMemberAsync(Guid userId, CancellationToken ct) =>
        // MemberUserIds is a native BSON array here, unlike the relational
        // providers' JSON-column fallback (ADR-0033) — the driver translates
        // AnyEq into a native $elemMatch/$in against the array, so this runs
        // server-side, not the MySQL provider's load-everything-and-filter
        // workaround.
        await database.UserGroups
            .Find(Builders<UserGroup>.Filter.AnyEq(g => g.MemberUserIds, userId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(UserGroup group, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await database.UserGroups
            .Find(g => g.Key == group.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.UserGroups.InsertOneAsync(group, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        var replacement = new UserGroup
        {
            Id = existing.Id,
            Key = existing.Key,
            Name = group.Name,
            Description = group.Description,
            MemberUserIds = group.MemberUserIds,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = group.UpdatedAt,
        };

        await database.UserGroups.ReplaceOneAsync(g => g.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await database.UserGroups.DeleteOneAsync(g => g.Key == key, ct).ConfigureAwait(false);
    }
}
