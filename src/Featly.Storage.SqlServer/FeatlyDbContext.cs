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
/// This is PR 1 of the SQL Server provider (issue #274): <see cref="Project"/>,
/// <see cref="Environment"/>, and <see cref="Flag"/> only, mirroring how the
/// Postgres provider's PR 1 (issue #157) started. The remaining entities
/// (configs, segments, experiments, RBAC, approvals, webhooks, audit,
/// settings), the <c>SqlServerFeatlyStore</c> facade, and
/// <c>AddFeatlySqlServerStore()</c> DI wiring land in follow-up PRs.
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
