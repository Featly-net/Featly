namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="RoleAssignment"/> entities — the
/// polymorphic join between an assignee (user or group) and a role, scoped to
/// a project and optionally an environment.
/// </summary>
/// <remarks>
/// The permission checker's hot path is <see cref="ListForAssigneesAsync"/>:
/// given the set of assignee ids relevant to a request (the user's own id plus
/// the ids of every group the user belongs to), return every assignment so the
/// checker can filter by scope and union the roles. M7 PR 7A only passes the
/// user's id; PR 7B adds group ids.
/// </remarks>
public interface IRoleAssignmentStore
{
    /// <summary>Returns the assignment with the given row id, or <c>null</c>.</summary>
    Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns every assignment whose <see cref="RoleAssignment.AssigneeId"/> is
    /// in <paramref name="assigneeIds"/>. The caller filters by project /
    /// environment scope and unions the matching roles. Returns an empty list
    /// when <paramref name="assigneeIds"/> is empty.
    /// </summary>
    Task<IReadOnlyList<RoleAssignment>> ListForAssigneesAsync(IReadOnlyCollection<Guid> assigneeIds, CancellationToken ct);

    /// <summary>Lists every assignment for a single assignee (user or group).</summary>
    Task<IReadOnlyList<RoleAssignment>> ListForAssigneeAsync(Guid assigneeId, CancellationToken ct);

    /// <summary>Lists every assignment scoped to the given project.</summary>
    Task<IReadOnlyList<RoleAssignment>> ListForProjectAsync(Guid projectId, CancellationToken ct);

    /// <summary>Inserts a new assignment. Throws if the id collides.</summary>
    Task CreateAsync(RoleAssignment assignment, CancellationToken ct);

    /// <summary>Deletes an assignment by row id. Idempotent for a missing id.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}
