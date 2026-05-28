namespace Featly;

/// <summary>
/// A request filed by a user asking for a role in a target project (and
/// optionally a specific environment), with a justification. An admin approves
/// it — which mints the corresponding <see cref="RoleAssignment"/> — or rejects
/// it with a reason (ARCHITECTURE.md §11).
/// </summary>
/// <remarks>
/// The Admin shortcut bypasses this flow entirely: an admin can create role
/// assignments directly through the role-assignments endpoint. The request flow
/// exists for users who lack the permission to assign themselves a role.
/// </remarks>
public sealed class RoleUpgradeRequest
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Row id of the <see cref="User"/> that filed the request.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Target project the requested role would be scoped to.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Target environment, or <c>null</c> for all environments in the project.</summary>
    public Guid? TargetEnvironmentId { get; init; }

    /// <summary>The role the user is requesting.</summary>
    public required Guid RequestedRoleId { get; init; }

    /// <summary>Free-text justification shown to the approving admin.</summary>
    public string? Justification { get; set; }

    /// <summary>Current state of the request.</summary>
    public RoleUpgradeStatus Status { get; set; }

    /// <summary>Row id of the admin that approved or rejected the request.</summary>
    public Guid? DecidedByUserId { get; set; }

    /// <summary>Optional comment the admin left when deciding (especially on reject).</summary>
    public string? DecisionComment { get; set; }

    /// <summary>Audit: when the request was filed.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: when the request was decided, if it has been.</summary>
    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>Lifecycle states of a <see cref="RoleUpgradeRequest"/>.</summary>
public enum RoleUpgradeStatus
{
    /// <summary>Awaiting an admin decision.</summary>
    Pending,

    /// <summary>Approved — a matching <see cref="RoleAssignment"/> was created.</summary>
    Approved,

    /// <summary>Rejected — see <see cref="RoleUpgradeRequest.DecisionComment"/>.</summary>
    Rejected,
}
