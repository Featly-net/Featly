using System.Text.Json;

namespace Featly;

/// <summary>
/// A proposed mutation to a flag / config / segment awaiting approval
/// (ARCHITECTURE.md §12). Created when the target environment's
/// <see cref="ApprovalPolicy"/> has <see cref="ApprovalPolicy.Required"/> set;
/// applied (mutating the underlying entity) once the policy is satisfied and an
/// editor clicks Apply.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ProposedState"/> and <see cref="CurrentState"/> are stored as raw
/// JSON so the change engine stays agnostic of the entity shape — the apply
/// step deserializes <see cref="ProposedState"/> back into the concrete entity
/// keyed by <see cref="EntityType"/>.
/// </para>
/// <para>
/// The change goes <c>Pending → Approved → Applied</c>, or <c>Pending →
/// Rejected</c>. If the underlying entity changes between Approved and Apply the
/// change becomes <see cref="ChangeStatus.Stale"/> and the author must rebase by
/// re-proposing.
/// </para>
/// </remarks>
public sealed class PendingChange
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Entity kind being changed: <c>"Flag"</c>, <c>"Config"</c>, <c>"Segment"</c>, …</summary>
    public required string EntityType { get; init; }

    /// <summary>Key of the entity being changed.</summary>
    public required string EntityKey { get; init; }

    /// <summary>Environment the change targets.</summary>
    public required Guid EnvironmentId { get; init; }

    /// <summary>Whether the change creates, updates, or archives the entity.</summary>
    public required ChangeAction Action { get; init; }

    /// <summary>The desired end state, as raw JSON (the entity's write shape).</summary>
    public required JsonElement ProposedState { get; init; }

    /// <summary>Snapshot of the entity at propose time, for diffing. <c>null</c> for a Create.</summary>
    public JsonElement? CurrentState { get; init; }

    /// <summary>Row id of the user that proposed the change.</summary>
    public required Guid AuthorUserId { get; init; }

    /// <summary>Optional message the author attached when proposing.</summary>
    public string? AuthorMessage { get; set; }

    /// <summary>Lifecycle state.</summary>
    public ChangeStatus Status { get; set; }

    /// <summary>Approval / rejection / request-changes decisions submitted against this change.</summary>
    public List<ChangeApproval> Approvals { get; set; } = [];

    /// <summary>Free-text discussion thread.</summary>
    public List<ChangeComment> Comments { get; set; } = [];

    /// <summary>Row id of the user that applied the change, once applied.</summary>
    public Guid? AppliedByUserId { get; set; }

    /// <summary>When the change was applied.</summary>
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>When the change was rejected.</summary>
    public DateTimeOffset? RejectedAt { get; set; }

    /// <summary>Reason supplied on rejection.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Set when the change was applied via emergency bypass (skips the approval gate).</summary>
    public bool WasEmergencyBypass { get; set; }

    /// <summary>Reason supplied with an emergency bypass.</summary>
    public string? EmergencyReason { get; set; }

    /// <summary>Audit: when the change was proposed.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification (new approval, comment, status change).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>What a <see cref="PendingChange"/> does to its target entity.</summary>
public enum ChangeAction
{
    /// <summary>Create a new entity.</summary>
    Create,

    /// <summary>Update an existing entity.</summary>
    Update,

    /// <summary>Archive (soft-delete) an existing entity.</summary>
    Archive,
}

/// <summary>Lifecycle states of a <see cref="PendingChange"/>.</summary>
public enum ChangeStatus
{
    /// <summary>Awaiting approvals.</summary>
    Pending,

    /// <summary>Policy satisfied; ready to apply.</summary>
    Approved,

    /// <summary>Rejected by an approver.</summary>
    Rejected,

    /// <summary>Applied to the underlying entity.</summary>
    Applied,

    /// <summary>The underlying entity changed after approval; must be re-proposed.</summary>
    Stale,
}

/// <summary>A single approval decision submitted against a <see cref="PendingChange"/>.</summary>
public sealed class ChangeApproval
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>The change this decision applies to.</summary>
    public required Guid PendingChangeId { get; init; }

    /// <summary>Row id of the approving user.</summary>
    public required Guid ApproverUserId { get; init; }

    /// <summary>The decision.</summary>
    public required ApprovalDecision Decision { get; init; }

    /// <summary>Optional comment attached to the decision.</summary>
    public string? Comment { get; set; }

    /// <summary>When the decision was submitted.</summary>
    public DateTimeOffset At { get; init; }
}

/// <summary>The three decisions an approver can submit.</summary>
public enum ApprovalDecision
{
    /// <summary>Approve the change.</summary>
    Approve,

    /// <summary>Reject the change outright.</summary>
    Reject,

    /// <summary>Ask the author for changes (does not count toward approval, does not reject).</summary>
    RequestChanges,
}

/// <summary>A free-text comment on a <see cref="PendingChange"/>.</summary>
public sealed class ChangeComment
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>The change this comment belongs to.</summary>
    public required Guid PendingChangeId { get; init; }

    /// <summary>Row id of the comment author.</summary>
    public required Guid AuthorUserId { get; init; }

    /// <summary>Comment text.</summary>
    public required string Body { get; init; }

    /// <summary>When the comment was posted.</summary>
    public DateTimeOffset At { get; init; }
}
