using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoPendingChangeStore(MongoFeatlyDatabase database) : IPendingChangeStore
{
    public async Task<PendingChange?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.PendingChanges
            .Find(c => c.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PendingChange>> ListAsync(CancellationToken ct) =>
        await database.PendingChanges
            .Find(FilterDefinition<PendingChange>.Empty)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PendingChange>> ListByStatusAsync(ChangeStatus status, CancellationToken ct) =>
        await database.PendingChanges
            .Find(c => c.Status == status)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PendingChange>> ListByEnvironmentAsync(Guid environmentId, CancellationToken ct) =>
        await database.PendingChanges
            .Find(c => c.EnvironmentId == environmentId)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PendingChange>> ListOpenForEntityAsync(string entityType, string entityKey, Guid environmentId, CancellationToken ct) =>
        await database.PendingChanges
            .Find(c => c.EnvironmentId == environmentId
                && c.EntityType == entityType
                && c.EntityKey == entityKey
                && (c.Status == ChangeStatus.Pending || c.Status == ChangeStatus.Approved))
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task CreateAsync(PendingChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        await database.PendingChanges.InsertOneAsync(change, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PendingChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);

        var update = Builders<PendingChange>.Update
            .Set(c => c.Status, change.Status)
            .Set(c => c.AuthorMessage, change.AuthorMessage)
            .Set(c => c.Approvals, change.Approvals)
            .Set(c => c.Comments, change.Comments)
            .Set(c => c.AppliedByUserId, change.AppliedByUserId)
            .Set(c => c.AppliedAt, change.AppliedAt)
            .Set(c => c.RejectedAt, change.RejectedAt)
            .Set(c => c.RejectionReason, change.RejectionReason)
            .Set(c => c.WasEmergencyBypass, change.WasEmergencyBypass)
            .Set(c => c.EmergencyReason, change.EmergencyReason)
            .Set(c => c.ScheduledApplyAt, change.ScheduledApplyAt)
            .Set(c => c.UpdatedAt, change.UpdatedAt);

        await database.PendingChanges
            .UpdateOneAsync(c => c.Id == change.Id, update, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryClaimStatusAsync(Guid id, ChangeStatus from, ChangeStatus to, CancellationToken ct)
    {
        // A single atomic conditional update — the document-store equivalent
        // of the relational providers' `UPDATE ... WHERE status=@from`
        // (issue #237): on a shared database only one concurrent caller's
        // filter matches, so exactly one caller claims the change.
        var update = Builders<PendingChange>.Update
            .Set(c => c.Status, to)
            .Set(c => c.UpdatedAt, DateTimeOffset.UtcNow);

        var result = await database.PendingChanges
            .UpdateOneAsync(c => c.Id == id && c.Status == from, update, cancellationToken: ct)
            .ConfigureAwait(false);

        return result.ModifiedCount == 1;
    }
}
