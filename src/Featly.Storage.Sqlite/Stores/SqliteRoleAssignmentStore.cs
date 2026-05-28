using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteRoleAssignmentStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IRoleAssignmentStore
{
    public async Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RoleAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoleAssignment>> ListForAssigneesAsync(IReadOnlyCollection<Guid> assigneeIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assigneeIds);
        if (assigneeIds.Count == 0)
        {
            return [];
        }

        var ids = assigneeIds as IList<Guid> ?? [.. assigneeIds];
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RoleAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.AssigneeId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoleAssignment>> ListForAssigneeAsync(Guid assigneeId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RoleAssignments.AsNoTracking()
            .Where(a => a.AssigneeId == assigneeId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoleAssignment>> ListForProjectAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RoleAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task CreateAsync(RoleAssignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.RoleAssignments
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }
        db.RoleAssignments.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
