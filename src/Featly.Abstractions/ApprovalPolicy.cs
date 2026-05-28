namespace Featly;

/// <summary>
/// The approval rules in force for a single <see cref="Environment"/>
/// (ARCHITECTURE.md §12). When <see cref="Required"/> is set, mutations to that
/// environment create a <see cref="PendingChange"/> instead of applying
/// directly, and the change can only be applied once
/// <see cref="ApproverRules"/> and <see cref="MinApprovals"/> are satisfied.
/// </summary>
public sealed class ApprovalPolicy
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Environment this policy governs. One policy per environment.</summary>
    public required Guid EnvironmentId { get; init; }

    /// <summary>When <c>true</c>, mutations require approval before they apply.</summary>
    public bool Required { get; set; }

    /// <summary>Minimum total approvals required, regardless of rules. Defaults to 1.</summary>
    public int MinApprovals { get; set; } = 1;

    /// <summary>When <c>false</c> (default), the author's own approval doesn't count toward satisfaction.</summary>
    public bool AuthorCanApproveOwnChange { get; set; }

    /// <summary>When <c>true</c> (default), an admin with <see cref="Permission.ChangeBypass"/> can apply immediately with a reason.</summary>
    public bool AllowEmergencyBypass { get; set; } = true;

    /// <summary>Structured approver requirements (specific user / any-from-role / any-from-group), each optionally mandatory.</summary>
    public List<ApproverRule> ApproverRules { get; set; } = [];
}

/// <summary>
/// One requirement within an <see cref="ApprovalPolicy"/>: who must approve and
/// how many. Combined with <see cref="Mandatory"/> to express "Carlos must
/// approve AND at least 1 from the Security group".
/// </summary>
public sealed class ApproverRule
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Whether the rule targets a specific user, any user with a role, or any member of a group.</summary>
    public required ApproverRuleType Type { get; init; }

    /// <summary>Target user, when <see cref="Type"/> is <see cref="ApproverRuleType.SpecificUser"/>.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Target role, when <see cref="Type"/> is <see cref="ApproverRuleType.AnyFromRole"/>.</summary>
    public Guid? RoleId { get; set; }

    /// <summary>Target group, when <see cref="Type"/> is <see cref="ApproverRuleType.AnyFromGroup"/>.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>When <c>true</c>, this rule must be satisfied for the change to be approvable (a hard requirement).</summary>
    public bool Mandatory { get; set; }

    /// <summary>How many distinct approvers matching this rule are needed. Defaults to 1.</summary>
    public int MinFromThisRule { get; set; } = 1;
}

/// <summary>The three shapes an <see cref="ApproverRule"/> can take.</summary>
public enum ApproverRuleType
{
    /// <summary>A named individual must approve.</summary>
    SpecificUser,

    /// <summary>At least <see cref="ApproverRule.MinFromThisRule"/> users holding a given role must approve.</summary>
    AnyFromRole,

    /// <summary>At least <see cref="ApproverRule.MinFromThisRule"/> members of a given group must approve.</summary>
    AnyFromGroup,
}
