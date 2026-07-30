namespace Featly.Storage.MongoDB;

/// <summary>
/// The <see cref="IFeatlyStore"/> facade for the MongoDB provider (ADR-0034):
/// it composes the per-entity sub-stores and holds nothing else. Unlike the
/// relational providers' shared <c>EfFeatlyStore</c>, this is a dedicated
/// type rather than a linked source file — this provider isn't EF Core, so
/// reusing a type from the <c>Featly.Storage.EntityFramework</c> namespace
/// would be misleading even though the composition shape is identical.
/// </summary>
internal sealed class MongoFeatlyStore(
    IFlagStore flags,
    IProjectStore projects,
    IEnvironmentStore environments,
    ISegmentStore segments,
    IConfigStore configs,
    IUserStore users,
    IRoleStore roles,
    IRoleAssignmentStore roleAssignments,
    IUserGroupStore groups,
    IRoleUpgradeRequestStore roleUpgradeRequests,
    IPendingChangeStore pendingChanges,
    IApprovalPolicyStore approvalPolicies,
    IExperimentStore experiments,
    IEventStore events,
    IAssignmentStore assignments,
    IWebhookStore webhooks,
    IWebhookDeliveryStore webhookDeliveries,
    IAuditStore audit,
    IApiKeyStore apiKeys,
    ISystemSettingsStore settings,
    IChangeNotifier changes) : IFeatlyStore
{
    public IFlagStore Flags { get; } = flags;

    public IProjectStore Projects { get; } = projects;

    public IEnvironmentStore Environments { get; } = environments;

    public ISegmentStore Segments { get; } = segments;

    public IConfigStore Configs { get; } = configs;

    public IUserStore Users { get; } = users;

    public IRoleStore Roles { get; } = roles;

    public IRoleAssignmentStore RoleAssignments { get; } = roleAssignments;

    public IUserGroupStore Groups { get; } = groups;

    public IRoleUpgradeRequestStore RoleUpgradeRequests { get; } = roleUpgradeRequests;

    public IPendingChangeStore PendingChanges { get; } = pendingChanges;

    public IApprovalPolicyStore ApprovalPolicies { get; } = approvalPolicies;

    public IExperimentStore Experiments { get; } = experiments;

    public IEventStore Events { get; } = events;

    public IAssignmentStore Assignments { get; } = assignments;

    public IWebhookStore Webhooks { get; } = webhooks;

    public IWebhookDeliveryStore WebhookDeliveries { get; } = webhookDeliveries;

    public IAuditStore Audit { get; } = audit;

    public IApiKeyStore ApiKeys { get; } = apiKeys;

    public ISystemSettingsStore Settings { get; } = settings;

    public IChangeNotifier Changes { get; } = changes;
}
