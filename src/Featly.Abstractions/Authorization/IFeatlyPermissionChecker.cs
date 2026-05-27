namespace Featly.Authorization;

/// <summary>
/// Returns whether the resolved user holds the asked-for permission in the
/// context of a project / environment. The default implementation
/// aggregates the user's direct <c>RoleAssignment</c> rows plus assignments
/// inherited from <c>UserGroup</c> membership, takes the union of
/// <c>Role.Permissions</c>, and returns whether the asked permission is in
/// that union.
/// </summary>
/// <remarks>
/// <para>
/// Wildcard environment assignments (environmentId == null) match any
/// environment within the project. Permissions are cumulative — there are
/// no deny rules per ARCHITECTURE.md §11. If Maria is Viewer from one
/// assignment and Admin from another she is effectively Admin.
/// </para>
/// <para>
/// M6 PR 6A defines the contract. The full implementation (with role
/// assignments, group resolution, project/env scoping) ships in M6 PR 6C
/// alongside the enforcement on every admin endpoint.
/// </para>
/// </remarks>
public interface IFeatlyPermissionChecker
{
    /// <summary>
    /// Returns <c>true</c> if the user holds the asked-for permission in the
    /// (project, environment) scope.
    /// </summary>
    Task<bool> HasAsync(
        ResolvedUser user,
        Guid projectId,
        Guid? environmentId,
        Permission permission,
        CancellationToken ct);
}
