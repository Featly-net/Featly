using Featly.Storage.SqlServer.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.SqlServer;

/// <summary>
/// EF Core context for the SQL Server Featly store. Marked <c>internal</c> on
/// purpose: consumers depend on <see cref="IFeatlyStore"/> and its sub-stores,
/// never on EF Core types directly. This is a separate type from the other
/// providers' own internal <c>FeatlyDbContext</c> — each provider owns its
/// context so entity configurations can use provider-native column types
/// (SQL Server's native <c>datetimeoffset</c>, JSON columns via
/// <c>nvarchar(max)</c>) without compromise. See ADR-0032.
/// </summary>
/// <remarks>
/// This is PR 5 of the SQL Server provider (issue #274) — the last entity
/// batch: <see cref="Experiment"/>, <see cref="Event"/>,
/// <see cref="Assignment"/>, <see cref="WebhookEndpoint"/>,
/// <see cref="WebhookDelivery"/>, and <see cref="AuditEntry"/> join the
/// entities from PR 1-4 (<see cref="Project"/>, <see cref="Environment"/>,
/// <see cref="Flag"/>, <see cref="Segment"/>, <see cref="Config"/>, RBAC,
/// the approval workflow, <see cref="ApiKey"/>, <see cref="SystemSetting"/>) —
/// every entity <c>IFeatlyStore</c> needs is now mapped. The
/// <c>SqlServerFeatlyStore</c> facade and <c>AddFeatlySqlServerStore()</c> DI
/// extension are a separate follow-up PR, same reasoning as the Postgres
/// provider's own equivalent split.
/// </remarks>
internal sealed class FeatlyDbContext(DbContextOptions<FeatlyDbContext> options)
    : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<Flag> Flags => Set<Flag>();

    public DbSet<Segment> Segments => Set<Segment>();

    public DbSet<Config> Configs => Set<Config>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    public DbSet<RoleUpgradeRequest> RoleUpgradeRequests => Set<RoleUpgradeRequest>();

    public DbSet<PendingChange> PendingChanges => Set<PendingChange>();

    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<Experiment> Experiments => Set<Experiment>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new EnvironmentConfiguration());
        modelBuilder.ApplyConfiguration(new FlagConfiguration());
        modelBuilder.ApplyConfiguration(new SegmentConfiguration());
        modelBuilder.ApplyConfiguration(new ConfigConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new RoleAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new UserGroupConfiguration());
        modelBuilder.ApplyConfiguration(new RoleUpgradeRequestConfiguration());
        modelBuilder.ApplyConfiguration(new PendingChangeConfiguration());
        modelBuilder.ApplyConfiguration(new ApprovalPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new SystemSettingConfiguration());
        modelBuilder.ApplyConfiguration(new ExperimentConfiguration());
        modelBuilder.ApplyConfiguration(new EventConfiguration());
        modelBuilder.ApplyConfiguration(new AssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookEndpointConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
    }
}
