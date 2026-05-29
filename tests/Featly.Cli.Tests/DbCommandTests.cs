using Featly.Cli;
using Featly.Storage.Sqlite;
using FluentAssertions;
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
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"featly-cli-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_dbPath}";

    [Fact]
    public async Task Migrate_on_fresh_database_applies_every_migration()
    {
        var exitCode = await CliApp.RunAsync(["db", "migrate", "--connection-string", _dbPath]);

        exitCode.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Pending.Should().BeEmpty();
        status.Applied.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Migrate_is_idempotent_when_already_up_to_date()
    {
        (await CliApp.RunAsync(["db", "migrate", "-c", _dbPath])).Should().Be(0);

        var secondRun = await CliApp.RunAsync(["db", "migrate", "-c", _dbPath]);

        secondRun.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_on_fresh_database_reports_all_pending()
    {
        var exitCode = await CliApp.RunAsync(["db", "status", "-c", _dbPath]);

        exitCode.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Rollback_to_initial_reverts_every_migration()
    {
        await CliApp.RunAsync(["db", "migrate", "-c", _dbPath]);

        var exitCode = await CliApp.RunAsync(["db", "rollback", "0", "-c", _dbPath, "--yes"]);

        exitCode.Should().Be(0);
        var status = await SqliteMigrationRunner.GetStatusAsync(ConnectionString, TestContext.Current.CancellationToken);
        status.Applied.Should().BeEmpty();
        status.Pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Drop_deletes_the_database_file()
    {
        await CliApp.RunAsync(["db", "migrate", "-c", _dbPath]);
        File.Exists(_dbPath).Should().BeTrue();

        var exitCode = await CliApp.RunAsync(["db", "drop", "-c", _dbPath, "-y"]);

        exitCode.Should().Be(0);
        File.Exists(_dbPath).Should().BeFalse();
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of throwaway test artifacts.
            }
        }
    }
}
