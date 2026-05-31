namespace Featly.Storage;

/// <summary>
/// Aggregate facade exposing the storage sub-stores. Application code (server,
/// CLI) depends only on this interface; concrete implementations live in
/// provider packages (<c>Featly.Storage.InMemory</c>, <c>Featly.Storage.Sqlite</c>).
/// </summary>
/// <remarks>
/// This type intentionally shares its name with the marker
/// <see cref="Featly.IFeatlyStore"/> in <c>Featly.Abstractions</c>; the marker
/// lets non-storage assemblies reference <see cref="IFeatlyStore"/> without
/// pulling in a storage dependency. The full facade is the one defined here.
/// </remarks>
public interface IFeatlyStore : Featly.IFeatlyStore
{
    /// <summary>Persistence operations on flags.</summary>
    IFlagStore Flags { get; }

    /// <summary>Persistence operations on projects.</summary>
    IProjectStore Projects { get; }

    /// <summary>Persistence operations on environments.</summary>
    IEnvironmentStore Environments { get; }

    /// <summary>Persistence operations on segments.</summary>
    ISegmentStore Segments { get; }

    /// <summary>Persistence operations on dynamic configs.</summary>
    IConfigStore Configs { get; }

    /// <summary>Persistence operations on users (M6+).</summary>
    IUserStore Users { get; }

    /// <summary>Persistence operations on roles (M6+).</summary>
    IRoleStore Roles { get; }

    /// <summary>Persistence operations on role assignments (M7+).</summary>
    IRoleAssignmentStore RoleAssignments { get; }

    /// <summary>Persistence operations on user groups (M7+).</summary>
    IUserGroupStore Groups { get; }

    /// <summary>Persistence operations on role upgrade requests (M7+).</summary>
    IRoleUpgradeRequestStore RoleUpgradeRequests { get; }

    /// <summary>Persistence operations on pending changes / approvals (M8+).</summary>
    IPendingChangeStore PendingChanges { get; }

    /// <summary>Persistence operations on per-environment approval policies (M8+).</summary>
    IApprovalPolicyStore ApprovalPolicies { get; }

    /// <summary>Persistence operations on experiments (M9+).</summary>
    IExperimentStore Experiments { get; }

    /// <summary>Append-only telemetry event store (M9+).</summary>
    IEventStore Events { get; }

    /// <summary>Persistence operations on sticky experiment assignments (M9+).</summary>
    IAssignmentStore Assignments { get; }

    /// <summary>Persistence operations on registered webhook endpoints (M10+).</summary>
    IWebhookStore Webhooks { get; }

    /// <summary>The persisted webhook delivery queue (M10+).</summary>
    IWebhookDeliveryStore WebhookDeliveries { get; }

    /// <summary>Append-only audit log (M10+).</summary>
    IAuditStore Audit { get; }

    /// <summary>Persistence operations on API keys (M6+).</summary>
    IApiKeyStore ApiKeys { get; }

    /// <summary>DB-overridable settings singletons (ARCHITECTURE.md §15).</summary>
    ISystemSettingsStore Settings { get; }

    /// <summary>In-process change notification stream feeding SSE.</summary>
    IChangeNotifier Changes { get; }
}
