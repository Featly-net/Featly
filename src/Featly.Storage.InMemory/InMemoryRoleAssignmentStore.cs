using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryRoleAssignmentStore : IRoleAssignmentStore
{
    private readonly ConcurrentDictionary<Guid, RoleAssignment> _byId = new();

    public Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var a) ? a : null);

    public Task<IReadOnlyList<RoleAssignment>> ListForAssigneesAsync(IReadOnlyCollection<Guid> assigneeIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assigneeIds);
        if (assigneeIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RoleAssignment>>([]);
        }

        var set = assigneeIds as HashSet<Guid> ?? [.. assigneeIds];
        var list = _byId.Values.Where(a => set.Contains(a.AssigneeId)).ToList();
        return Task.FromResult<IReadOnlyList<RoleAssignment>>(list);
    }

    public Task<IReadOnlyList<RoleAssignment>> ListForAssigneeAsync(Guid assigneeId, CancellationToken ct)
    {
        var list = _byId.Values.Where(a => a.AssigneeId == assigneeId).ToList();
        return Task.FromResult<IReadOnlyList<RoleAssignment>>(list);
    }

    public Task<IReadOnlyList<RoleAssignment>> ListForProjectAsync(Guid projectId, CancellationToken ct)
    {
        var list = _byId.Values.Where(a => a.ProjectId == projectId).ToList();
        return Task.FromResult<IReadOnlyList<RoleAssignment>>(list);
    }

    public Task CreateAsync(RoleAssignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (!_byId.TryAdd(assignment.Id, assignment))
        {
            throw new InvalidOperationException($"RoleAssignment '{assignment.Id}' already exists.");
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        _byId.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
