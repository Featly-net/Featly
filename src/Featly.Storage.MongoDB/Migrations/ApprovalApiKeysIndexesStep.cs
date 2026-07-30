using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// Fourth migration step (PR 4, issue #277): indexes for the approval
/// workflow and API key entities. <see cref="SystemSetting"/> needs no index
/// beyond its own <c>_id</c> (<see cref="SystemSetting.Key"/> is the natural
/// key, mapped directly as the document id).
/// </summary>
internal sealed class ApprovalApiKeysIndexesStep : IMongoMigrationStep
{
    public string Name => "0004_approval_apikeys_indexes";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);

        var pendingChanges = database.GetCollection<PendingChange>(MongoCollectionNames.PendingChanges);
        await pendingChanges.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<PendingChange>(Builders<PendingChange>.IndexKeys.Ascending(c => c.Status)),
                new CreateIndexModel<PendingChange>(Builders<PendingChange>.IndexKeys.Ascending(c => c.EnvironmentId)),
                new CreateIndexModel<PendingChange>(
                    Builders<PendingChange>.IndexKeys.Ascending(c => c.Status).Ascending(c => c.ScheduledApplyAt)),
            ],
            cancellationToken: ct).ConfigureAwait(false);

        var approvalPolicies = database.GetCollection<ApprovalPolicy>(MongoCollectionNames.ApprovalPolicies);
        await approvalPolicies.Indexes.CreateOneAsync(
            new CreateIndexModel<ApprovalPolicy>(
                Builders<ApprovalPolicy>.IndexKeys.Ascending(p => p.EnvironmentId),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: ct).ConfigureAwait(false);

        var apiKeys = database.GetCollection<ApiKey>(MongoCollectionNames.ApiKeys);
        await apiKeys.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ApiKey>(Builders<ApiKey>.IndexKeys.Ascending(k => k.Prefix)),
                new CreateIndexModel<ApiKey>(Builders<ApiKey>.IndexKeys.Ascending(k => k.EnvironmentId)),
            ],
            cancellationToken: ct).ConfigureAwait(false);
    }
}
