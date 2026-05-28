using System.Text.Json;

namespace Featly;

/// <summary>
/// An in-process notification that a consequential action happened
/// (ARCHITECTURE.md §17). Emitted by the server after a successful mutation and
/// fanned out to two consumers: the audit recorder (persists an
/// <see cref="AuditEntry"/>) and the webhook dispatcher (enqueues a
/// <see cref="WebhookDelivery"/> for each matching endpoint). Distinct from the
/// SSE <c>ChangeNotification</c>, which exists only to invalidate SDK snapshots.
/// </summary>
public sealed class FeatlyDomainEvent
{
    /// <summary>The event type (e.g. <c>flag.updated</c>). See <see cref="FeatlyEventTypes"/>.</summary>
    public required string Type { get; init; }

    /// <summary>The kind of entity acted on (e.g. <c>Flag</c>).</summary>
    public required string EntityType { get; init; }

    /// <summary>The entity's key/identifier, when it has one.</summary>
    public string? EntityKey { get; init; }

    /// <summary>The environment the action was scoped to, when applicable.</summary>
    public Guid? EnvironmentId { get; init; }

    /// <summary>Identifier of who performed the action.</summary>
    public string? ActorIdentifier { get; init; }

    /// <summary>Optional structured detail (before/after snapshot or payload).</summary>
    public JsonElement? Data { get; init; }

    /// <summary>When the event occurred (server clock). Defaults to now when unset.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Well-known <see cref="FeatlyDomainEvent.Type"/> values. Dotted, lowercase,
/// GitHub-webhook style. Webhook endpoints subscribe to a subset of these (an
/// empty subscription means all).
/// </summary>
public static class FeatlyEventTypes
{
    /// <summary>A flag was created.</summary>
    public const string FlagCreated = "flag.created";

    /// <summary>A flag was updated.</summary>
    public const string FlagUpdated = "flag.updated";

    /// <summary>A config was created.</summary>
    public const string ConfigCreated = "config.created";

    /// <summary>A config was updated.</summary>
    public const string ConfigUpdated = "config.updated";

    /// <summary>A config was archived.</summary>
    public const string ConfigArchived = "config.archived";

    /// <summary>A segment was created.</summary>
    public const string SegmentCreated = "segment.created";

    /// <summary>A segment was updated.</summary>
    public const string SegmentUpdated = "segment.updated";

    /// <summary>A segment was deleted.</summary>
    public const string SegmentDeleted = "segment.deleted";

    /// <summary>An experiment was created.</summary>
    public const string ExperimentCreated = "experiment.created";

    /// <summary>An experiment was updated.</summary>
    public const string ExperimentUpdated = "experiment.updated";

    /// <summary>An experiment was started.</summary>
    public const string ExperimentStarted = "experiment.started";

    /// <summary>An experiment was stopped.</summary>
    public const string ExperimentStopped = "experiment.stopped";

    /// <summary>A change was proposed for approval.</summary>
    public const string ChangeProposed = "change.proposed";

    /// <summary>A change was approved.</summary>
    public const string ChangeApproved = "change.approved";

    /// <summary>A change was rejected.</summary>
    public const string ChangeRejected = "change.rejected";

    /// <summary>A change was applied to the underlying entity.</summary>
    public const string ChangeApplied = "change.applied";

    /// <summary>A role was assigned to a user or group.</summary>
    public const string RoleAssigned = "role.assigned";

    /// <summary>A role assignment was removed.</summary>
    public const string RoleUnassigned = "role.unassigned";
}
