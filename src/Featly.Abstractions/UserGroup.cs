namespace Featly;

/// <summary>
/// A named set of <see cref="User"/> ids. Groups let an admin assign a role to
/// many users at once: a <see cref="RoleAssignment"/> with
/// <see cref="AssigneeType.Group"/> grants its role to every member.
/// </summary>
/// <remarks>
/// <para>
/// Membership is stored inline as a list of user ids (<see cref="MemberUserIds"/>).
/// At v0.0.x scale a group has tens of members, not millions, so a separate
/// join table is unnecessary; the permission checker reads the member list to
/// discover which groups a user belongs to.
/// </para>
/// <para>
/// M7 PR 7B introduces the entity, storage, and group-aware permission
/// resolution. Admin endpoints to manage groups land in M7 PR 7C.
/// </para>
/// </remarks>
public sealed class UserGroup
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Unique key used by APIs and assignments.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; set; }

    /// <summary>Optional description shown in the dashboard.</summary>
    public string? Description { get; set; }

    /// <summary>Row ids of the <see cref="User"/> members. A user inherits every role assigned to a group it belongs to.</summary>
    public List<Guid> MemberUserIds { get; set; } = [];

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
