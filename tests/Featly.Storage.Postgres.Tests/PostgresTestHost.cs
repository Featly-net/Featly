using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Spins up a throwaway Postgres database (unique name per test), migrates it
/// with the real EF Core migrations, and exposes the three PR-1 stores
/// directly (there is no public facade yet — see <c>FeatlyDbContext</c>'s
/// remarks). Disposing drops the database.
/// </summary>
/// <remarks>
/// The server connection comes from the <c>FEATLY_POSTGRES_TEST_HOST</c> /
/// <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> environment variables (set
/// by the CI service container); sensible localhost defaults let this run
/// against a local Postgres for development.
/// </remarks>
internal sealed class PostgresTestHost : IAsyncDisposable
{
    private readonly string _databaseName;
    private readonly string _adminConnectionString;

    private PostgresTestHost(string databaseName, string adminConnectionString, string connectionString)
    {
        _databaseName = databaseName;
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public Stores.PostgresProjectStore ProjectStore => new(CreateFactory());

    public Stores.PostgresEnvironmentStore EnvironmentStore => new(CreateFactory());

    public Stores.PostgresFlagStore FlagStore => new(CreateFactory());

    public Stores.PostgresSegmentStore SegmentStore => new(CreateFactory());

    public Stores.PostgresConfigStore ConfigStore => new(CreateFactory());

    public Stores.PostgresUserStore UserStore => new(CreateFactory());

    public Stores.PostgresRoleStore RoleStore => new(CreateFactory());

    public Stores.PostgresRoleAssignmentStore RoleAssignmentStore => new(CreateFactory());

    public Stores.PostgresUserGroupStore UserGroupStore => new(CreateFactory());

    public Stores.PostgresRoleUpgradeRequestStore RoleUpgradeRequestStore => new(CreateFactory());

    public static async Task<PostgresTestHost> CreateAsync(CancellationToken ct = default)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = System.Environment.GetEnvironmentVariable("FEATLY_POSTGRES_TEST_HOST") ?? "localhost",
            Port = int.TryParse(System.Environment.GetEnvironmentVariable("FEATLY_POSTGRES_TEST_PORT"), out var port) ? port : 15432,
            Username = System.Environment.GetEnvironmentVariable("FEATLY_POSTGRES_TEST_USER") ?? "featly",
            Password = System.Environment.GetEnvironmentVariable("FEATLY_POSTGRES_TEST_PASSWORD") ?? "featly",
            Database = "postgres",
        };
        var adminConnectionString = builder.ConnectionString;

        var databaseName = $"featly_test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync(ct).ConfigureAwait(false);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        builder.Database = databaseName;
        var connectionString = builder.ConnectionString;

        var options = new DbContextOptionsBuilder<FeatlyDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new FeatlyDbContext(options))
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }

        return new PostgresTestHost(databaseName, adminConnectionString, connectionString);
    }

    private IDbContextFactory<FeatlyDbContext> CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<FeatlyDbContext>(builder => builder.UseNpgsql(ConnectionString));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<FeatlyDbContext>>();
    }

    public async ValueTask DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync().ConfigureAwait(false);
        // Terminate any lingering connections to the test database (pooled
        // connections from the DbContext factory) so DROP DATABASE doesn't fail.
        await using (var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid()", admin))
        {
            terminate.Parameters.AddWithValue("db", _databaseName);
            await terminate.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", admin);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
