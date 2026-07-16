using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Covers <see cref="PostgresMigrationRunner"/> — the offline surface
/// <c>featly db --provider postgres</c> sits on top of (issue #257) — against a
/// real, throwaway Postgres database.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresMigrationRunnerTests
{
    [Fact]
    public async Task GetStatus_on_an_empty_schema_reports_every_migration_pending()
    {
        // PostgresTestHost.CreateAsync already migrates its throwaway database
        // (every other test in this project wants a working schema to exercise
        // entities against), so an empty schema here means rolling back rather
        // than a truly fresh CreateAsync.
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await PostgresMigrationRunner.RollbackAsync(host.ConnectionString, PostgresMigrationRunner.InitialDatabaseTarget, ct);

        var status = await PostgresMigrationRunner.GetStatusAsync(host.ConnectionString, ct);

        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Migrate_applies_every_pending_migration_and_is_idempotent()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await PostgresMigrationRunner.MigrateAsync(host.ConnectionString, ct);

        var status = await PostgresMigrationRunner.GetStatusAsync(host.ConnectionString, ct);
        status.Pending.Should().BeEmpty();
        status.Applied.Should().NotBeEmpty();

        // Re-running against an up-to-date schema is a no-op, not an error.
        await PostgresMigrationRunner.MigrateAsync(host.ConnectionString, ct);
        (await PostgresMigrationRunner.GetStatusAsync(host.ConnectionString, ct)).Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Rollback_to_initial_reverts_every_migration()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await PostgresMigrationRunner.MigrateAsync(host.ConnectionString, ct);

        await PostgresMigrationRunner.RollbackAsync(host.ConnectionString, PostgresMigrationRunner.InitialDatabaseTarget, ct);

        var status = await PostgresMigrationRunner.GetStatusAsync(host.ConnectionString, ct);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Drop_deletes_the_database()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await PostgresMigrationRunner.MigrateAsync(host.ConnectionString, ct);

        var dropped = await PostgresMigrationRunner.DropAsync(host.ConnectionString, ct);

        dropped.Should().BeTrue();
        // Dropping an already-gone database is a no-op, not an error — mirrors
        // SqliteMigrationRunner.DropAsync.
        (await PostgresMigrationRunner.DropAsync(host.ConnectionString, ct)).Should().BeFalse();
    }
}
