using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoUserStore(MongoFeatlyDatabase database) : IUserStore
{
    public async Task<User?> GetByIdentifierAsync(string identifier, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return await database.Users
            .Find(u => u.Identifier == identifier)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.Users
            .Find(u => u.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) =>
        await database.Users
            .Find(FilterDefinition<User>.Empty)
            .SortBy(u => u.Identifier)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(User user, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = actor;

        var existing = await database.Users
            .Find(u => u.Identifier == user.Identifier)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.Users.InsertOneAsync(user, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        var replacement = new User
        {
            Id = existing.Id,
            Identifier = existing.Identifier,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Disabled = user.Disabled,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            CreatedBy = existing.CreatedBy,
            UpdatedBy = user.UpdatedBy,
        };

        await database.Users.ReplaceOneAsync(u => u.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DisableAsync(string identifier, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var update = Builders<User>.Update
            .Set(u => u.Disabled, true)
            .Set(u => u.UpdatedAt, DateTimeOffset.UtcNow)
            .Set(u => u.UpdatedBy, actor);

        await database.Users
            .UpdateOneAsync(u => u.Identifier == identifier, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }
}
