using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// Sixth migration step (PR 7, issue #277): creates the capped
/// <see cref="MongoCollectionNames.ChangeNotifications"/> collection
/// <see cref="MongoChangeNotifier"/> and <see cref="MongoChangeListenerHostedService"/>
/// use as their Change Streams signal board. Must be capped at creation
/// time — a collection can't be converted to capped afterwards without
/// recreating it, so this can't ride on a later, more general step.
/// </summary>
internal sealed class ChangeNotificationsCollectionStep : IMongoMigrationStep
{
    public string Name => "0006_change_notifications_collection";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(database);

        await database.CreateCollectionAsync(
            MongoCollectionNames.ChangeNotifications,
            new CreateCollectionOptions { Capped = true, MaxSize = 1_048_576 },
            ct).ConfigureAwait(false);
    }
}
