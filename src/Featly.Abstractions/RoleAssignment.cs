namespace Featly;

/// <summary>
/// Associates a <see cref="Role"/> with an assignee (a <see cref="User"/> or a
/// <c>UserGroup</c>) in the scope of a <see cref="Project"/> and, optionally,
/// a single <see cref="Environment"/>. The polymorphic join table at the heart
/// of Featly's RBAC (ARCHITECTURE.md §11).
/// </summary>
/// <remarks>
/// <para>
/// Effective permissions are the <em>union</em> of <see cref="Role.Permissions"/>
/// across every assignment that matches the target project and an environment
/// of either the target or <c>null</c> (the wildcard, meaning "all environments
/// in this project"). There are no deny rules — more is more. If one assignment
/// grants <c>Viewer</c> and another grants <c>Admin</c>, the user is effectively
/// <c>Admin</c>.
/// </para>
/// <para>
/// M7 PR 7A introduces direct <see cref="AssigneeType.User"/> assignments and
/// the resolver that unions them. Group assignments
/// (<see cref="AssigneeType.Group"/>) are resolved in M7 PR 7B once
/// <c>UserGroup</c> lands.
/// </para>
/// </remarks>
public sealed class RoleAssignment
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Whether <see cref="AssigneeId"/> points at a <see cref="User"/> or a <c>UserGroup</c>.</summary>
    public required AssigneeType AssigneeType { get; init; }

    /// <summary>Row id of the assigned <see cref="User"/> or <c>UserGroup</c>, per <see cref="AssigneeType"/>.</summary>
    public required Guid AssigneeId { get; init; }

    /// <summary>Project this assignment scopes to.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// Environment this assignment scopes to, or <c>null</c> for the wildcard
    /// (all environments in <see cref="ProjectId"/>).
    /// </summary>
    public Guid? EnvironmentId { get; init; }

    /// <summary>The granted role.</summary>
    public required Guid RoleId { get; init; }

    /// <summary>Audit: when the assignment was created.</summary>
    public DateTimeOffset AssignedAt { get; init; }

    /// <summary>Audit: row id of the user that created the assignment, if known.</summary>
    public Guid? AssignedByUserId { get; init; }
}

/// <summary>Discriminator for the polymorphic <see cref="RoleAssignment.AssigneeId"/>.</summary>
public enum AssigneeType
{
    /// <summary>The assignee is a single <see cref="User"/>.</summary>
    User,

    /// <summary>The assignee is a <c>UserGroup</c>; every member inherits the role.</summary>
    Group,
}
