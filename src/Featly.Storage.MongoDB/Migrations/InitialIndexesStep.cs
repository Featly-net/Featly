using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// First migration step (PR 1, issue #277): unique indexes for the three
/// entities this slice ships — the document-store equivalent of the
/// relational providers' <c>InitialCreate</c> migration.
/// </summary>
internal sealed class InitialIndexesStep : IMongoMigrationStep
{
    public string Name => "0001_initial_indexes";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);

        var projects = database.GetCollection<Project>(MongoCollectionNames.Projects);
        await projects.Indexes.CreateOneAsync(
            new CreateIndexModel<Project>(
                Builders<Project>.IndexKeys.Ascending(p => p.Key),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var environments = database.GetCollection<Environment>(MongoCollectionNames.Environments);
        await environments.Indexes.CreateOneAsync(
            new CreateIndexModel<Environment>(
                Builders<Environment>.IndexKeys.Ascending(e => e.ProjectId).Ascending(e => e.Key),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var flags = database.GetCollection<Flag>(MongoCollectionNames.Flags);
        await flags.Indexes.CreateOneAsync(
            new CreateIndexModel<Flag>(
                Builders<Flag>.IndexKeys.Ascending(f => f.EnvironmentId).Ascending(f => f.Key),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);
    }
}
