using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Tests;

/// <summary>
/// Spins up a throwaway MongoDB database (unique name per test) against a
/// shared replica-set instance, applies the migration steps, and exposes the
/// three PR-1 stores directly (there is no public facade yet). Disposing
/// drops the database.
/// </summary>
/// <remarks>
/// The server connection comes from the
/// <c>FEATLY_MONGODB_TEST_CONNECTION_STRING</c> environment variable (set by
/// the CI service container); a sensible single-node-replica-set localhost
/// default lets this run against a local MongoDB for development. This
/// provider requires a replica set for transactions and Change Streams
/// (ADR-0034) — a standalone <c>mongod</c> will fail migration with a clear
/// driver error, not silently degrade.
/// </remarks>
internal sealed class MongoTestHost : IAsyncDisposable
{
    private const string DefaultConnectionString = "mongodb://localhost:27017/?replicaSet=rs0";

    private readonly IMongoClient _client;
    private readonly string _databaseName;

    private MongoTestHost(IMongoClient client, string databaseName, MongoFeatlyDatabase database)
    {
        _client = client;
        _databaseName = databaseName;
        Database = database;
    }

    public MongoFeatlyDatabase Database { get; }

    public Stores.MongoProjectStore ProjectStore => new(Database);

    public Stores.MongoEnvironmentStore EnvironmentStore => new(Database);

    public Stores.MongoFlagStore FlagStore => new(Database);

    public static async Task<MongoTestHost> CreateAsync(CancellationToken ct = default)
    {
        var baseConnectionString = System.Environment.GetEnvironmentVariable("FEATLY_MONGODB_TEST_CONNECTION_STRING") ?? DefaultConnectionString;
        var databaseName = $"featly_test_{Guid.NewGuid():N}";
        var connectionString = new MongoUrlBuilder(baseConnectionString) { DatabaseName = databaseName }.ToString();

        await MongoMigrationRunner.MigrateAsync(connectionString, ct).ConfigureAwait(false);

        var client = new MongoClient(connectionString);
        var database = new MongoFeatlyDatabase(client.GetDatabase(databaseName));

        return new MongoTestHost(client, databaseName, database);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DropDatabaseAsync(_databaseName).ConfigureAwait(false);
    }
}
