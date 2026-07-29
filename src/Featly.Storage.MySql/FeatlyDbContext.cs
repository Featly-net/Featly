using Featly.Storage.MySql.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.MySql;

/// <summary>
/// EF Core context for the MySQL/MariaDB Featly store. Marked <c>internal</c>
/// on purpose: consumers depend on <see cref="IFeatlyStore"/> and its
/// sub-stores, never on EF Core types directly. This is a separate type from
/// the other providers' own internal <c>FeatlyDbContext</c> — each provider
/// owns its context so entity configurations can use provider-native column
/// types (MySQL's native <c>JSON</c>, UTC-normalized <c>DATETIME(6)</c>)
/// without compromise. See ADR-0033.
/// </summary>
/// <remarks>
/// PR 4 of the MySQL provider (issue #276) adds the approval workflow
/// (<see cref="PendingChange"/>, <see cref="ApprovalPolicy"/>),
/// <see cref="ApiKey"/>, and <see cref="SystemSetting"/> to PR 1/2/3's
/// <see cref="Project"/>/<see cref="Environment"/>/<see cref="Flag"/>/
/// <see cref="Segment"/>/<see cref="Config"/>/RBAC surface. The remaining
/// entities, the <c>MySqlFeatlyStore</c> facade, and
/// <c>AddFeatlyMySqlStore()</c> DI wiring land in follow-up PRs.
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
    }
}
