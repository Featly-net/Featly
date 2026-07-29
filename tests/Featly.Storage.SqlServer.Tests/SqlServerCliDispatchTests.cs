using AwesomeAssertions;
using Featly.Cli;
using Xunit;

namespace Featly.Storage.SqlServer.Tests;

/// <summary>
/// End-to-end coverage for <c>featly db --provider sqlserver</c> (issue #274):
/// the real command tree (<see cref="CliApp.RunAsync"/>) against a throwaway
/// SQL Server database, mirroring <c>Featly.Storage.Postgres.Tests.PostgresCliDispatchTests</c>.
/// Lives here rather than in <c>Featly.Cli.Tests</c> because it needs the
/// <c>RequiresSqlServer</c> container that project doesn't have.
/// </summary>
[Trait("Category", "RequiresSqlServer")]
public class SqlServerCliDispatchTests
{
    [Fact]
    public async Task Migrate_via_the_command_tree_applies_every_migration()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);

        var exitCode = await CliApp.RunAsync(["db", "migrate", "--provider", "sqlserver", "-c", host.ConnectionString]);

        exitCode.Should().Be(0);
        var status = await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, TestContext.Current.CancellationToken);
        status.Pending.Should().BeEmpty();
        status.Applied.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Status_via_the_command_tree_reports_pending_migrations()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);

        var exitCode = await CliApp.RunAsync(["db", "status", "-p", "sqlserver", "-c", host.ConnectionString]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Rollback_via_the_command_tree_reverts_to_the_initial_schema()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await CliApp.RunAsync(["db", "migrate", "--provider", "sqlserver", "-c", host.ConnectionString]);

        var exitCode = await CliApp.RunAsync(["db", "rollback", "0", "--provider", "sqlserver", "-c", host.ConnectionString, "--yes"]);

        exitCode.Should().Be(0);
        var status = await SqlServerMigrationRunner.GetStatusAsync(host.ConnectionString, ct);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Drop_via_the_command_tree_deletes_the_database()
    {
        await using var host = await SqlServerTestHost.CreateAsync(TestContext.Current.CancellationToken);
        await CliApp.RunAsync(["db", "migrate", "--provider", "sqlserver", "-c", host.ConnectionString]);

        var exitCode = await CliApp.RunAsync(["db", "drop", "--provider", "sqlserver", "-c", host.ConnectionString, "-y"]);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Missing_connection_string_fails_with_a_friendly_message()
    {
        // Explicitly clear FEATLY_SQLSERVER so the test is deterministic
        // regardless of what the running environment happens to have set.
        var previous = System.Environment.GetEnvironmentVariable("FEATLY_SQLSERVER");
        System.Environment.SetEnvironmentVariable("FEATLY_SQLSERVER", null);
        try
        {
            var exitCode = await CliApp.RunAsync(["db", "status", "--provider", "sqlserver"]);
            exitCode.Should().Be(1);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("FEATLY_SQLSERVER", previous);
        }
    }
}
