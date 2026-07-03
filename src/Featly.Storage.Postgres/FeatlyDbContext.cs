using Featly.Storage.Postgres.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres;

/// <summary>
/// EF Core context for the PostgreSQL Featly store. Marked <c>internal</c> on
/// purpose: consumers depend on <see cref="IFeatlyStore"/> and its sub-stores,
/// never on EF Core types directly. This is a separate type from
/// <c>Featly.Storage.Sqlite</c>'s own internal <c>FeatlyDbContext</c> — each
/// provider owns its context so entity configurations can use provider-native
/// column types (<c>jsonb</c>, <c>timestamptz</c>) without compromise. See
/// ADR-0026.
/// </summary>
/// <remarks>
/// This is PR 5 of the Postgres provider (issue #157) — the last entity
/// batch. <see cref="Project"/>, <see cref="Environment"/>, and
/// <see cref="Flag"/> shipped in PR 1; <see cref="Segment"/> and
/// <see cref="Config"/> in PR 2; the RBAC entities in PR 3; the
/// approval-workflow entities plus <see cref="ApiKey"/>/<see cref="SystemSetting"/>
/// in PR 4. <see cref="Experiment"/>, <see cref="Event"/>,
/// <see cref="Assignment"/>, <see cref="WebhookEndpoint"/>,
/// <see cref="WebhookDelivery"/>, and <see cref="AuditEntry"/> land here —
/// every entity <c>IFeatlyStore</c> needs. The <c>PostgresFeatlyStore</c>
/// facade and <c>AddFeatlyPostgresStore()</c> DI extension, plus a real
/// Postgres <c>LISTEN</c>/<c>NOTIFY</c>-backed <c>IChangeNotifier</c> (ADR-0026;
/// the in-process notifier the other 18 sub-stores don't need doesn't carry
/// over from SQLite), are a separate follow-up PR — a persistent background
/// listener connection is a materially different piece of infrastructure
/// than an EF Core store and deserves its own review.
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
