using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Featly.Storage.MongoDB;

/// <summary>
/// <see cref="IChangeNotifier"/> for the MongoDB provider (ADR-0034): local
/// subscribers fan out through the same <see cref="InProcessChangeNotifier"/>
/// every provider uses, but every notification is also written to the capped
/// <see cref="MongoCollectionNames.ChangeNotifications"/> collection so
/// replicas other than the one that made the change hear about it too, via
/// <see cref="MongoChangeListenerHostedService"/>'s Change Stream.
/// </summary>
/// <remarks>
/// <see cref="NotifyAsync"/> only publishes — it does not fan out to local
/// subscribers directly, mirroring <c>PostgresChangeNotifier</c>. Delivery to
/// local subscribers happens exclusively through
/// <see cref="DispatchLocallyAsync"/>, which <see cref="MongoChangeListenerHostedService"/>
/// calls for every insert its Change Stream observes — including inserts this
/// same process made, since a Change Stream reports every write against the
/// watched collection regardless of which client made it. One symmetric
/// fan-out path, not two, so there is nothing to deduplicate.
/// </remarks>
internal sealed class MongoChangeNotifier(MongoFeatlyDatabase database) : IChangeNotifier
{
    private readonly InProcessChangeNotifier _local = new();

    public async ValueTask NotifyAsync(ChangeNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = JsonSerializer.Serialize(notification);
        var document = new BsonDocument
        {
            { "payload", payload },
            { "at", DateTime.UtcNow },
        };

        await database.Database.GetCollection<BsonDocument>(MongoCollectionNames.ChangeNotifications)
            .InsertOneAsync(document, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public IDisposable Subscribe(Func<ChangeNotification, CancellationToken, ValueTask> handler) => _local.Subscribe(handler);

    /// <summary>
    /// Delivers a notification observed on the Change Stream — from any
    /// replica, including this one — to this process's local subscribers.
    /// Called only by <see cref="MongoChangeListenerHostedService"/>.
    /// </summary>
    internal ValueTask DispatchLocallyAsync(ChangeNotification notification, CancellationToken ct) =>
        _local.NotifyAsync(notification, ct);
}
