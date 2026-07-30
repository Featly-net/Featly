using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.MongoDB.Tests;

/// <summary>
/// Direct coverage for <c>MongoMigrationRunner</c> — <see cref="MongoTestHost"/>
/// already exercises <c>MigrateAsync</c> for every test in this assembly (it
/// applies migrations before handing out stores), so this class covers the
/// parts that path doesn't: reading status directly, and the connection-
/// string validation that runs before any database call.
/// </summary>
[Trait("Category", "RequiresMongoDB")]
public class MongoMigrationRunnerTests
{
    [Fact]
    public async Task GetStatusAsync_reports_every_step_applied_after_a_fresh_migrate()
    {
        // MongoTestHost.CreateAsync already called MigrateAsync once to set up
        // this throwaway database.
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var status = await MongoMigrationRunner.GetStatusAsync(host.ConnectionString, ct);

        status.Pending.Should().BeEmpty();
        status.Applied.Should().Contain("0001_initial_indexes");
    }

    [Fact]
    public void MigrateAsync_rejects_a_connection_string_without_a_database_name()
    {
        var migrate = async () => await MongoMigrationRunner.MigrateAsync("mongodb://localhost:27017/", TestContext.Current.CancellationToken);

        migrate.Should().ThrowAsync<ArgumentException>().WithMessage("*database name*");
    }

    [Fact]
    public async Task RollbackAsync_is_not_supported()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);

        var rollback = async () => await MongoMigrationRunner.RollbackAsync(host.ConnectionString, "0", TestContext.Current.CancellationToken);

        await rollback.Should().ThrowAsync<NotSupportedException>().WithMessage("*not supported*");
    }

    [Fact]
    public async Task DropAsync_deletes_an_existing_database_and_is_idempotent_for_a_missing_one()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var dropped = await MongoMigrationRunner.DropAsync(host.ConnectionString, ct);
        dropped.Should().BeTrue();

        // The database no longer exists -- a second drop is a no-op, not an error.
        var droppedAgain = await MongoMigrationRunner.DropAsync(host.ConnectionString, ct);
        droppedAgain.Should().BeFalse();
    }
}
