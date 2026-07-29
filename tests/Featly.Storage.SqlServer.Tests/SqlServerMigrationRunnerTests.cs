using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.SqlServer.Tests;

/// <summary>
/// Covers <see cref="SqlServerMigrationRunner"/> — the offline surface
/// <c>featly db --provider sqlserver</c> sits on top of (issue #274) — against
/// a real, throwaway SQL Server database.
/// </summary>
[Trait("Category", "RequiresSqlServer")]
public class SqlServerMigrationRunnerTests
{
    [Fact]
    public async Task GetStatus_on_an_empty_schema_reports_every_migration_pending()
    {
        // SqlServerTestHost.CreateAsync already migrates its throwaway database
        // (every other test in this project wants a working schema to exercise
        // entities against), so an empty schema here means rolling back rather
        // than a truly fresh CreateAsync.
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await SqlServerMigrationRunner.RollbackAsync(host.ConnectionString, SqlServerMigrationRunner.InitialDatabaseTarget, ct);

        var status = await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, ct);

        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Migrate_applies_every_pending_migration_and_is_idempotent()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await SqlServerMigrationRunner.MigrateAsync(host.ConnectionString, ct);

        var status = await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, ct);
        status.Pending.Should().BeEmpty();
        status.Applied.Should().NotBeEmpty();

        // Re-running against an up-to-date schema is a no-op, not an error.
        await SqlServerMigrationRunner.MigrateAsync(host.ConnectionString, ct);
        (await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, ct)).Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Rollback_to_initial_reverts_every_migration()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await SqlServerMigrationRunner.MigrateAsync(host.ConnectionString, ct);

        await SqlServerMigrationRunner.RollbackAsync(host.ConnectionString, SqlServerMigrationRunner.InitialDatabaseTarget, ct);

        var status = await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, ct);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Drop_deletes_the_database()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await SqlServerMigrationRunner.MigrateAsync(host.ConnectionString, ct);

        var dropped = await SqlServerMigrationRunner.DropAsync(host.ConnectionString, ct);

        dropped.Should().BeTrue();
        // Dropping an already-gone database is a no-op, not an error — mirrors
        // SqliteMigrationRunner.DropAsync.
        (await SqlServerMigrationRunner.DropAsync(host.ConnectionString, ct)).Should().BeFalse();
    }
}
