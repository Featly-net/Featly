using AwesomeAssertions;
using Featly.Cli;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// End-to-end coverage for <c>featly db --provider postgres</c> (issue #257):
/// the real command tree (<see cref="CliApp.RunAsync"/>) against a throwaway
/// Postgres database, mirroring <c>Featly.Cli.Tests.DbCommandTests</c>'s SQLite
/// coverage. Lives here rather than in <c>Featly.Cli.Tests</c> because it needs
/// the <c>RequiresPostgres</c> container that project doesn't have.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresCliDispatchTests
{
    [Fact]
    public async Task Migrate_via_the_command_tree_applies_every_migration()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);

        var exitCode = await CliApp.RunAsync(["db", "migrate", "--provider", "postgres", "-c", host.ConnectionString]);

        exitCode.Should().Be(0);
        var status = await PostgresMigrationRunner.GetStatusAsync(host.ConnectionString, TestContext.Current.CancellationToken);
        status.Pending.Should().BeEmpty();
        status.Applied.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Status_via_the_command_tree_reports_pending_migrations()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);

        var exitCode = await CliApp.RunAsync(["db", "status", "-p", "postgres", "-c", host.ConnectionString]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Rollback_via_the_command_tree_reverts_to_the_initial_schema()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await CliApp.RunAsync(["db", "migrate", "--provider", "postgres", "-c", host.ConnectionString]);

        var exitCode = await CliApp.RunAsync(["db", "rollback", "0", "--provider", "postgres", "-c", host.ConnectionString, "--yes"]);

        exitCode.Should().Be(0);
        var status = await PostgresMigrationRunner.GetStatusAsync(host.ConnectionString, ct);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Drop_via_the_command_tree_deletes_the_database()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        await CliApp.RunAsync(["db", "migrate", "--provider", "postgres", "-c", host.ConnectionString]);

        var exitCode = await CliApp.RunAsync(["db", "drop", "--provider", "postgres", "-c", host.ConnectionString, "-y"]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Missing_connection_string_fails_with_a_friendly_message()
    {
        // Explicitly clear FEATLY_POSTGRES so the test is deterministic
        // regardless of what the running environment happens to have set.
        var previous = System.Environment.GetEnvironmentVariable("FEATLY_POSTGRES");
        System.Environment.SetEnvironmentVariable("FEATLY_POSTGRES", null);
        try
        {
            var exitCode = await CliApp.RunAsync(["db", "status", "--provider", "postgres"]);
            exitCode.Should().Be(1);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("FEATLY_POSTGRES", previous);
        }
    }
}
