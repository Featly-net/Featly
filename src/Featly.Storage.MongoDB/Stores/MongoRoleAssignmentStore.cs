using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoRoleAssignmentStore(MongoFeatlyDatabase database) : IRoleAssignmentStore
{
    public async Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.RoleAssignments
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RoleAssignment>> ListForAssigneesAsync(IReadOnlyCollection<Guid> assigneeIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assigneeIds);
        if (assigneeIds.Count == 0)
        {
            return [];
        }

        return await database.RoleAssignments
            .Find(Builders<RoleAssignment>.Filter.In(a => a.AssigneeId, assigneeIds))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoleAssignment>> ListForAssigneeAsync(Guid assigneeId, CancellationToken ct) =>
        await database.RoleAssignments
            .Find(a => a.AssigneeId == assigneeId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RoleAssignment>> ListForProjectAsync(Guid projectId, CancellationToken ct) =>
        await database.RoleAssignments
            .Find(a => a.ProjectId == projectId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task CreateAsync(RoleAssignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await database.RoleAssignments.InsertOneAsync(assignment, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await database.RoleAssignments.DeleteOneAsync(a => a.Id == id, ct).ConfigureAwait(false);
    }
}
