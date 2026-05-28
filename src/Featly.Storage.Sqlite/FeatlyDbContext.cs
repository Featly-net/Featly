using Featly.Storage.Sqlite.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite;

/// <summary>
/// EF Core context for the SQLite Featly store. Marked <c>internal</c> on
/// purpose: consumers depend on <see cref="IFeatlyStore"/> and its sub-stores,
/// never on EF Core types directly. This keeps the public surface free of
/// storage-engine concerns and allows the internal mapping to evolve without
/// breaking changes.
/// </summary>
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
    }
}
