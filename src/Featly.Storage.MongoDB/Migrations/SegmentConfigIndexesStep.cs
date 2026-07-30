using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// Second migration step (PR 2, issue #277): unique indexes for the two
/// entities this slice ships.
/// </summary>
internal sealed class SegmentConfigIndexesStep : IMongoMigrationStep
{
    public string Name => "0002_segment_config_indexes";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);

        var segments = database.GetCollection<Segment>(MongoCollectionNames.Segments);
        await segments.Indexes.CreateOneAsync(
            new CreateIndexModel<Segment>(
                Builders<Segment>.IndexKeys.Ascending(s => s.EnvironmentId).Ascending(s => s.Key),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var configs = database.GetCollection<Config>(MongoCollectionNames.Configs);
        await configs.Indexes.CreateOneAsync(
            new CreateIndexModel<Config>(
                Builders<Config>.IndexKeys.Ascending(c => c.EnvironmentId).Ascending(c => c.Key),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);
    }
}
