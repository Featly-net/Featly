using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Featly.Storage.SqlServer.Tests;

/// <summary>
/// Spins up a throwaway SQL Server database (unique name per test), migrates
/// it with the real EF Core migrations, and exposes the three PR-1 stores
/// directly (there is no public facade yet — see <c>FeatlyDbContext</c>'s
/// remarks). Disposing drops the database.
/// </summary>
/// <remarks>
/// The server connection comes from the <c>FEATLY_SQLSERVER_TEST_HOST</c> /
/// <c>_PORT</c> / <c>_USER</c> / <c>_PASSWORD</c> environment variables (set
/// by the CI service container); sensible localhost defaults let this run
/// against a local SQL Server for development.
/// </remarks>
internal sealed class SqlServerTestHost : IAsyncDisposable
{
    private readonly string _databaseName;
    private readonly string _adminConnectionString;

    private SqlServerTestHost(string databaseName, string adminConnectionString, string connectionString)
    {
        _databaseName = databaseName;
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public Stores.SqlServerProjectStore ProjectStore => new(CreateFactory());

    public Stores.SqlServerEnvironmentStore EnvironmentStore => new(CreateFactory());

    public Stores.SqlServerFlagStore FlagStore => new(CreateFactory());

    public Stores.SqlServerSegmentStore SegmentStore => new(CreateFactory());

    public Stores.SqlServerConfigStore ConfigStore => new(CreateFactory());

    public Stores.SqlServerUserStore UserStore => new(CreateFactory());

    public Stores.SqlServerRoleStore RoleStore => new(CreateFactory());

    public Stores.SqlServerRoleAssignmentStore RoleAssignmentStore => new(CreateFactory());

    public Stores.SqlServerUserGroupStore UserGroupStore => new(CreateFactory());

    public Stores.SqlServerRoleUpgradeRequestStore RoleUpgradeRequestStore => new(CreateFactory());

    public Stores.SqlServerPendingChangeStore PendingChangeStore => new(CreateFactory());

    public Stores.SqlServerApprovalPolicyStore ApprovalPolicyStore => new(CreateFactory());

    public Stores.SqlServerApiKeyStore ApiKeyStore => new(CreateFactory());

    public Stores.SqlServerSystemSettingsStore SystemSettingsStore => new(CreateFactory());

    public static async Task<SqlServerTestHost> CreateAsync(CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{System.Environment.GetEnvironmentVariable("FEATLY_SQLSERVER_TEST_HOST") ?? "localhost"},{System.Environment.GetEnvironmentVariable("FEATLY_SQLSERVER_TEST_PORT") ?? "1433"}",
            UserID = System.Environment.GetEnvironmentVariable("FEATLY_SQLSERVER_TEST_USER") ?? "sa",
            Password = System.Environment.GetEnvironmentVariable("FEATLY_SQLSERVER_TEST_PASSWORD") ?? "Featly_Test_Pw1",
            TrustServerCertificate = true,
            InitialCatalog = "master",
        };
        var adminConnectionString = builder.ConnectionString;

        var databaseName = $"featly_test_{Guid.NewGuid():N}";
        await using (var admin = new SqlConnection(adminConnectionString))
        {
            await admin.OpenAsync(ct).ConfigureAwait(false);
            await using var create = new SqlCommand($"CREATE DATABASE [{databaseName}]", admin);
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        builder.InitialCatalog = databaseName;
        var connectionString = builder.ConnectionString;

        var options = new DbContextOptionsBuilder<FeatlyDbContext>().UseSqlServer(connectionString).Options;
        await using (var db = new FeatlyDbContext(options))
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }

        return new SqlServerTestHost(databaseName, adminConnectionString, connectionString);
    }

    private IDbContextFactory<FeatlyDbContext> CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<FeatlyDbContext>(builder => builder.UseSqlServer(ConnectionString));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<FeatlyDbContext>>();
    }

    public async ValueTask DisposeAsync()
    {
        await using var admin = new SqlConnection(_adminConnectionString);
        await admin.OpenAsync().ConfigureAwait(false);
        // Force the test database into single-user mode with rollback to kill
        // any lingering pooled connections from the DbContext factory, then
        // drop it — SQL Server refuses DROP DATABASE while sessions are open.
        await using (var terminate = new SqlCommand(
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE", admin))
        {
            await terminate.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await using var drop = new SqlCommand($"DROP DATABASE IF EXISTS [{_databaseName}]", admin);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
