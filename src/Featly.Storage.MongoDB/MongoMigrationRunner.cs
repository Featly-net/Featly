using Featly.Storage.MongoDB.Migrations;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Featly.Storage.MongoDB;

/// <summary>
/// Snapshot of the MongoDB "migration" state: which steps have already run,
/// and which the current build defines but has not yet applied.
/// </summary>
/// <param name="Applied">Step names recorded in <c>__migrations</c>, in the order <see cref="MongoMigrationSteps.All"/> defines them.</param>
/// <param name="Pending">Step names compiled into this build but not yet applied, in application order.</param>
public sealed record MongoMigrationStatus(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Pending);

/// <summary>
/// Public, offline entry point for operating the Featly MongoDB "schema"
/// without a running host — the surface `Featly.Cli`'s `db --provider mongodb`
/// command sits on top of (ADR-0034). Mongo has no schema to migrate, so this
/// walks <see cref="MongoMigrationSteps.All"/> in order, applying and
/// recording each one not yet present in the <c>__migrations</c> collection —
/// behaviorally equivalent to the relational providers' EF Core migration
/// runners, without pretending Mongo has a schema.
/// </summary>
public static class MongoMigrationRunner
{
    /// <summary>Applies every pending migration step. No-op when already up to date.</summary>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(connectionString, out var database);
        var migrations = MigrationsCollection(database);
        var applied = await GetAppliedNamesAsync(migrations, cancellationToken).ConfigureAwait(false);

        foreach (var step in MongoMigrationSteps.All)
        {
            if (applied.Contains(step.Name))
            {
                continue;
            }

            await step.ApplyAsync(database, cancellationToken).ConfigureAwait(false);
            var record = new BsonDocument
            {
                { "_id", step.Name },
                { "appliedAt", DateTime.UtcNow },
            };
            await migrations.InsertOneAsync(record, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the applied and pending step sets for the target database.</summary>
    public static async Task<MongoMigrationStatus> GetStatusAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(connectionString, out var database);
        var migrations = MigrationsCollection(database);
        var applied = await GetAppliedNamesAsync(migrations, cancellationToken).ConfigureAwait(false);
        var pending = MongoMigrationSteps.All
            .Select(s => s.Name)
            .Where(name => !applied.Contains(name))
            .ToList();

        return new MongoMigrationStatus([.. MongoMigrationSteps.All.Select(s => s.Name).Where(applied.Contains)], pending);
    }

    /// <summary>
    /// Not supported for this provider. Every relational provider's
    /// <c>RollbackAsync</c> reverts the schema using EF Core's generated
    /// down-migrations — a reverse operation the framework derives
    /// automatically from the schema diff. Mongo's migration steps
    /// (<see cref="MongoMigrationSteps"/>) are idempotent index/collection
    /// creation with no framework-derived inverse, and no natural one to
    /// hand-write either: "remove this index" or "drop this collection" is a
    /// real, destructive operation on live data, not a safe schema reversal.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public static Task RollbackAsync(string connectionString, string targetMigration, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Rollback is not supported for --provider mongodb: migration steps here are idempotent index/collection " +
            "creation, not reversible schema changes, so there is no 'down' action to run. " +
            "Use 'featly db drop' to reset the database entirely, or restore from a backup.");

    /// <summary>Deletes the target database entirely. Irreversible.</summary>
    /// <returns><c>true</c> if a database existed and was deleted; otherwise <c>false</c>.</returns>
    public static async Task<bool> DropAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(connectionString, out var database);
        var existed = await DatabaseExistsAsync(client, database.DatabaseNamespace.DatabaseName, cancellationToken).ConfigureAwait(false);
        await client.DropDatabaseAsync(database.DatabaseNamespace.DatabaseName, cancellationToken).ConfigureAwait(false);
        return existed;
    }

    private static async Task<bool> DatabaseExistsAsync(MongoClient client, string databaseName, CancellationToken ct)
    {
        using var cursor = await client.ListDatabaseNamesAsync(ct).ConfigureAwait(false);
        var names = await cursor.ToListAsync(ct).ConfigureAwait(false);
        return names.Contains(databaseName, StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> GetAppliedNamesAsync(IMongoCollection<BsonDocument> migrations, CancellationToken ct)
    {
        var docs = await migrations.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct).ConfigureAwait(false);
        return [.. docs.Select(d => d["_id"].AsString)];
    }

    private static IMongoCollection<BsonDocument> MigrationsCollection(IMongoDatabase database) =>
        database.GetCollection<BsonDocument>(MongoCollectionNames.Migrations);

    /// <summary>
    /// Creates a short-lived client for a single runner call. The caller owns
    /// disposal (<c>using var client = ...</c>) — <see cref="MongoClient"/>
    /// holds a connection pool that must be torn down explicitly, unlike the
    /// relational providers' <c>DbContext</c>, which EF Core's own
    /// <c>await using</c> pattern already handles.
    /// </summary>
    private static MongoClient CreateClient(string connectionString, out IMongoDatabase database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        MongoFeatlyDatabase.EnsureClassMapsRegistered();

        var mongoUrl = MongoUrl.Create(connectionString);
        if (string.IsNullOrWhiteSpace(mongoUrl.DatabaseName))
        {
            throw new ArgumentException(
                "The MongoDB connection string must include a database name (e.g. 'mongodb://host/featly').",
                nameof(connectionString));
        }

        var client = new MongoClient(mongoUrl);
        database = client.GetDatabase(mongoUrl.DatabaseName);
        return client;
    }
}
