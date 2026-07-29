using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Featly.Storage.MySql.Tests;

/// <summary>
/// Spins up a throwaway MySQL database (unique name per test), migrates it
/// with the real EF Core migrations, and exposes the three PR-1 stores
/// directly (there is no public facade yet — see <c>FeatlyDbContext</c>'s
/// remarks). Disposing drops the database.
/// </summary>
/// <remarks>
/// The server connection comes from the <c>FEATLY_MYSQL_TEST_HOST</c> /
/// <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> environment variables (set
/// by the CI service container); sensible localhost defaults let this run
/// against a local MySQL for development.
/// </remarks>
internal sealed class MySqlTestHost : IAsyncDisposable
{
    private readonly string _databaseName;
    private readonly string _adminConnectionString;

    private MySqlTestHost(string databaseName, string adminConnectionString, string connectionString)
    {
        _databaseName = databaseName;
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public Stores.MySqlProjectStore ProjectStore => new(CreateFactory());

    public Stores.MySqlEnvironmentStore EnvironmentStore => new(CreateFactory());

    public Stores.MySqlFlagStore FlagStore => new(CreateFactory());

    public Stores.MySqlSegmentStore SegmentStore => new(CreateFactory());

    public Stores.MySqlConfigStore ConfigStore => new(CreateFactory());

    public static async Task<MySqlTestHost> CreateAsync(CancellationToken ct = default)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = System.Environment.GetEnvironmentVariable("FEATLY_MYSQL_TEST_HOST") ?? "localhost",
            Port = uint.TryParse(System.Environment.GetEnvironmentVariable("FEATLY_MYSQL_TEST_PORT"), out var port) ? port : 13306,
            UserID = System.Environment.GetEnvironmentVariable("FEATLY_MYSQL_TEST_USER") ?? "root",
            Password = System.Environment.GetEnvironmentVariable("FEATLY_MYSQL_TEST_PASSWORD") ?? "Featly_Test_Pw1",
        };
        var adminConnectionString = builder.ConnectionString;

        var databaseName = $"featly_test_{Guid.NewGuid():N}";
        await using (var admin = new MySqlConnection(adminConnectionString))
        {
            await admin.OpenAsync(ct).ConfigureAwait(false);
            await using var create = new MySqlCommand($"CREATE DATABASE `{databaseName}`", admin);
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        builder.Database = databaseName;
        var connectionString = builder.ConnectionString;

        var options = new DbContextOptionsBuilder<FeatlyDbContext>()
            .UseMySql(connectionString, MySqlServerVersionInfo.Version)
            .Options;
        await using (var db = new FeatlyDbContext(options))
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }

        return new MySqlTestHost(databaseName, adminConnectionString, connectionString);
    }

    private IDbContextFactory<FeatlyDbContext> CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<FeatlyDbContext>(builder => builder.UseMySql(ConnectionString, MySqlServerVersionInfo.Version));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<FeatlyDbContext>>();
    }

    public async ValueTask DisposeAsync()
    {
        await using var admin = new MySqlConnection(_adminConnectionString);
        await admin.OpenAsync().ConfigureAwait(false);
        // Unlike SQL Server, MySQL allows dropping a database with other
        // sessions still connected to it, so no "kill connections first" step
        // is needed here.
        await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{_databaseName}`", admin);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
