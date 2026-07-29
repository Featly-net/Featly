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
/// PR 2 of the MySQL provider (issue #276) adds <see cref="Segment"/> and
/// <see cref="Config"/> to PR 1's <see cref="Project"/>/<see cref="Environment"/>/
/// <see cref="Flag"/>. The remaining entities, the <c>MySqlFeatlyStore</c>
/// facade, and <c>AddFeatlyMySqlStore()</c> DI wiring land in follow-up PRs.
/// </remarks>
internal sealed class FeatlyDbContext(DbContextOptions<FeatlyDbContext> options)
    : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<Flag> Flags => Set<Flag>();

    public DbSet<Segment> Segments => Set<Segment>();

    public DbSet<Config> Configs => Set<Config>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new EnvironmentConfiguration());
        modelBuilder.ApplyConfiguration(new FlagConfiguration());
        modelBuilder.ApplyConfiguration(new SegmentConfiguration());
        modelBuilder.ApplyConfiguration(new ConfigConfiguration());
    }
}
