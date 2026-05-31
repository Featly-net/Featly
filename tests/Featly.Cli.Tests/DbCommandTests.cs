using AwesomeAssertions;
using Featly.Cli;
using Featly.Storage.Sqlite;
using Xunit;

namespace Featly.Cli.Tests;

/// <summary>
/// End-to-end coverage for the offline <c>featly db</c> commands. Each test runs
/// the real command tree (<see cref="CliApp.RunAsync"/>) against a throwaway
/// SQLite file and asserts the resulting schema state through the public
/// <see cref="SqliteMigrationRunner"/> facade.
/// </summary>
public sealed class DbCommandTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("featly-cli").FullName;

    private string DbPath => Path.Join(_tempDir, "test.db");

    private string ConnectionString => $"Data Source={DbPath}";

    [Fact]
    public async Task Migrate_on_fresh_database_applies_every_migration()
    {
        var exitCode = await CliApp.RunAsync(["db", "migrate", "--connection-string", DbPath]);

        exitCode.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Pending.Should().BeEmpty();
        status.Applied.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Migrate_is_idempotent_when_already_up_to_date()
    {
        (await CliApp.RunAsync(["db", "migrate", "-c", DbPath])).Should().Be(0);

        var secondRun = await CliApp.RunAsync(["db", "migrate", "-c", DbPath]);

        secondRun.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_on_fresh_database_reports_all_pending()
    {
        var exitCode = await CliApp.RunAsync(["db", "status", "-c", DbPath]);

        exitCode.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Rollback_to_initial_reverts_every_migration()
    {
        await CliApp.RunAsync(["db", "migrate", "-c", DbPath]);

        var exitCode = await CliApp.RunAsync(["db", "rollback", "0", "-c", DbPath, "--yes"]);

        exitCode.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Drop_deletes_the_database_file()
    {
        await CliApp.RunAsync(["db", "migrate", "-c", DbPath]);
        File.Exists(DbPath).Should().BeTrue();

        var exitCode = await CliApp.RunAsync(["db", "drop", "-c", DbPath, "-y"]);

        exitCode.Should().Be(0);
        File.Exists(DbPath).Should().BeFalse();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of throwaway test artifacts.
        }
    }
}
