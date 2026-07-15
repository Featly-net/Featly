namespace Featly.Storage.EntityFramework;

/// <summary>
/// The <see cref="IFeatlyStore"/> facade for a relational provider: it composes
/// the per-entity sub-stores and holds nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Shared by SQLite and Postgres because there is nothing provider-specific here
/// — the facade never touches a <c>DbContext</c>; DI hands it whichever
/// provider's sub-stores are registered. Compiled into each provider assembly as
/// a linked source file, so ADR-0026's "DbContext is internal and per-provider"
/// still holds and the two assemblies stay decoupled.
/// </para>
/// <para>
/// Sub-stores open per-operation contexts via the registered
/// <c>IDbContextFactory&lt;FeatlyDbContext&gt;</c>, so this is safe as a singleton.
/// </para>
/// </remarks>
internal sealed class EfFeatlyStore(
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
