using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoRoleUpgradeRequestStore(MongoFeatlyDatabase database) : IRoleUpgradeRequestStore
{
    public async Task<RoleUpgradeRequest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.RoleUpgradeRequests
            .Find(r => r.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RoleUpgradeRequest>> ListAsync(CancellationToken ct) =>
        await database.RoleUpgradeRequests
            .Find(FilterDefinition<RoleUpgradeRequest>.Empty)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RoleUpgradeRequest>> ListByStatusAsync(RoleUpgradeStatus status, CancellationToken ct) =>
        await database.RoleUpgradeRequests
            .Find(r => r.Status == status)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task CreateAsync(RoleUpgradeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await database.RoleUpgradeRequests.InsertOneAsync(request, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RoleUpgradeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var update = Builders<RoleUpgradeRequest>.Update
            .Set(r => r.Justification, request.Justification)
            .Set(r => r.Status, request.Status)
            .Set(r => r.DecidedByUserId, request.DecidedByUserId)
            .Set(r => r.DecisionComment, request.DecisionComment)
            .Set(r => r.DecidedAt, request.DecidedAt);

        await database.RoleUpgradeRequests
            .UpdateOneAsync(r => r.Id == request.Id, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }
}
