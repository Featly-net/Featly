using System.Text.Json;

namespace Featly;

/// <summary>
/// An immutable audit-log record (ARCHITECTURE.md §17): one row per consequential
/// action — a flag/config/segment/experiment mutation, an approval decision, or
/// an RBAC change. Written by the audit recorder from the same
/// <see cref="FeatlyDomainEvent"/> stream that feeds outbound webhooks.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>When the action happened (server clock).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>The event type / action (e.g. <c>flag.updated</c>, <c>change.approved</c>).</summary>
    public required string Action { get; init; }

    /// <summary>The kind of entity acted on (e.g. <c>Flag</c>, <c>Config</c>, <c>Role</c>).</summary>
    public required string EntityType { get; init; }

    /// <summary>The entity's key/identifier, when it has one.</summary>
    public string? EntityKey { get; init; }

    /// <summary>The environment the action was scoped to, when applicable.</summary>
    public Guid? EnvironmentId { get; init; }

    /// <summary>Identifier of who performed the action (user identifier or api-key principal).</summary>
    public string? ActorIdentifier { get; init; }

    /// <summary>
    /// Optional structured detail — typically a before/after snapshot or the
    /// event payload. Stored as raw JSON so the dashboard can render a diff.
    /// </summary>
    public JsonElement? Data { get; init; }

    /// <summary>
    /// Monotonic append order within the tamper-evident hash chain (issue #208),
    /// assigned by the store on append. Orders the chain independently of
    /// <see cref="At"/> (which is the event clock and need not be unique).
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The <see cref="Hash"/> of the preceding chain entry, or <c>null</c> for the
    /// first entry (genesis, or the oldest surviving entry after retention pruning).
    /// Assigned by the store on append.
    /// </summary>
    public string? PreviousHash { get; set; }

    /// <summary>
    /// Tamper-evident SHA-256 (lowercase hex) over this entry's content plus
    /// <see cref="PreviousHash"/> (issue #208), assigned by the store on append.
    /// Recomputing it detects any post-hoc modification; a mismatched
    /// <see cref="PreviousHash"/> link detects deletion or reordering. <c>null</c>
    /// on legacy rows written before the chain existed.
    /// </summary>
    public string? Hash { get; set; }
}
