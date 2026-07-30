using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// Fifth migration step (PR 5, issue #277): indexes for the final entity
/// batch.
/// </summary>
internal sealed class FinalBatchIndexesStep : IMongoMigrationStep
{
    public string Name => "0005_final_batch_indexes";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);

        var experiments = database.GetCollection<Experiment>(MongoCollectionNames.Experiments);
        await experiments.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Experiment>(
                    Builders<Experiment>.IndexKeys.Ascending(e => e.EnvironmentId).Ascending(e => e.Key),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<Experiment>(Builders<Experiment>.IndexKeys.Ascending(e => e.EnvironmentId)),
            ],
            cancellationToken: ct).ConfigureAwait(false);

        var events = database.GetCollection<Event>(MongoCollectionNames.Events);
        await events.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Event>(Builders<Event>.IndexKeys.Ascending(e => e.EnvironmentId)),
                new CreateIndexModel<Event>(Builders<Event>.IndexKeys.Ascending(e => e.EnvironmentId).Ascending(e => e.Type)),
                new CreateIndexModel<Event>(Builders<Event>.IndexKeys.Ascending(e => e.EnvironmentId).Ascending(e => e.FlagKey)),
                new CreateIndexModel<Event>(Builders<Event>.IndexKeys.Ascending(e => e.EnvironmentId).Ascending(e => e.CustomKey)),
                new CreateIndexModel<Event>(Builders<Event>.IndexKeys.Ascending(e => e.SubjectKey)),
            ],
            cancellationToken: ct).ConfigureAwait(false);

        var assignments = database.GetCollection<Assignment>(MongoCollectionNames.Assignments);
        await assignments.Indexes.CreateOneAsync(
            new CreateIndexModel<Assignment>(
                Builders<Assignment>.IndexKeys.Ascending(a => a.ExperimentId).Ascending(a => a.SubjectKey),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var webhookEndpoints = database.GetCollection<WebhookEndpoint>(MongoCollectionNames.WebhookEndpoints);
        await webhookEndpoints.Indexes.CreateOneAsync(
            new CreateIndexModel<WebhookEndpoint>(Builders<WebhookEndpoint>.IndexKeys.Ascending(e => e.EnvironmentId)),
            cancellationToken: ct).ConfigureAwait(false);

        var webhookDeliveries = database.GetCollection<WebhookDelivery>(MongoCollectionNames.WebhookDeliveries);
        await webhookDeliveries.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<WebhookDelivery>(
                    Builders<WebhookDelivery>.IndexKeys.Ascending(d => d.Status).Ascending(d => d.NextAttemptAt)),
                new CreateIndexModel<WebhookDelivery>(Builders<WebhookDelivery>.IndexKeys.Ascending(d => d.WebhookEndpointId)),
            ],
            cancellationToken: ct).ConfigureAwait(false);

        var auditEntries = database.GetCollection<AuditEntry>(MongoCollectionNames.AuditEntries);
        await auditEntries.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<AuditEntry>(Builders<AuditEntry>.IndexKeys.Ascending(a => a.At)),
                new CreateIndexModel<AuditEntry>(Builders<AuditEntry>.IndexKeys.Ascending(a => a.EntityType).Ascending(a => a.EntityKey)),
                new CreateIndexModel<AuditEntry>(Builders<AuditEntry>.IndexKeys.Ascending(a => a.EnvironmentId)),
                new CreateIndexModel<AuditEntry>(Builders<AuditEntry>.IndexKeys.Ascending(a => a.Sequence)),
            ],
            cancellationToken: ct).ConfigureAwait(false);
    }
}
