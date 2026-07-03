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
/// This is PR 3 of the Postgres provider (issue #157): <see cref="Project"/>,
/// <see cref="Environment"/>, and <see cref="Flag"/> shipped in PR 1;
/// <see cref="Segment"/> and <see cref="Config"/> in PR 2; the RBAC entities
/// (<see cref="User"/>, <see cref="Role"/>, <see cref="RoleAssignment"/>,
/// <see cref="UserGroup"/>, <see cref="RoleUpgradeRequest"/>) land here. The
/// remaining entities land in follow-up PRs; only once every
/// <c>IFeatlyStore</c> sub-store has a Postgres implementation does a
/// <c>PostgresFeatlyStore</c> facade and <c>AddFeatlyPostgresStore()</c> DI
/// extension get added — the facade can't compile as a partial
/// implementation of <c>IFeatlyStore</c>.
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
    }
}
