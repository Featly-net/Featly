namespace Featly.Storage.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IFeatlyStore"/>. Aggregates
/// per-entity sub-stores. Created and disposed by the DI container; sub-stores
/// open per-operation <see cref="FeatlyDbContext"/> instances via the
/// registered <c>IDbContextFactory&lt;FeatlyDbContext&gt;</c>, so the facade
/// itself is safe to use as a singleton.
/// </summary>
internal sealed class SqliteFeatlyStore(
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
    IApiKeyStore apiKeys,
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

    public IApiKeyStore ApiKeys { get; } = apiKeys;

    public IChangeNotifier Changes { get; } = changes;
}
