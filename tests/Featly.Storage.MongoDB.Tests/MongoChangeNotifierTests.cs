using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Featly.Storage.MongoDB.Tests;

/// <summary>
/// Covers cross-replica change push (ADR-0034, issue #277): several
/// <see cref="MongoChangeNotifier"/> + <see cref="MongoChangeListenerHostedService"/>
/// pairs, each standing in for a server replica, against the same real
/// MongoDB replica set. A notification raised through one must be observed by
/// every other.
/// </summary>
[Trait("Category", "RequiresMongoDB")]
public class MongoChangeNotifierTests
{
    [Fact]
    public async Task Notification_raised_on_one_replica_is_observed_by_another()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await using var replicaA = await SimulatedReplica.StartAsync(host.ConnectionString, ct);
        await using var replicaB = await SimulatedReplica.StartAsync(host.ConnectionString, ct);

        var received = new List<ChangeNotification>();
        using var subscription = replicaB.Notifier.Subscribe((n, _) =>
        {
            received.Add(n);
            return ValueTask.CompletedTask;
        });

        var sent = new ChangeNotification(Guid.NewGuid(), "Flag", "cross-replica-flag", DateTimeOffset.UtcNow);
        await replicaA.Notifier.NotifyAsync(sent, ct);

        await PollUntilAsync(() => received.Count > 0, ct);

        received.Should().ContainSingle();
        received[0].Should().Be(sent);
    }

    [Fact]
    public async Task Notification_is_also_observed_by_the_replica_that_raised_it()
    {
        // NotifyAsync only publishes -- delivery to local subscribers happens
        // exclusively through the Change Stream round-trip, so the
        // originating replica must hear its own change back the same way
        // every other replica does.
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await using var replica = await SimulatedReplica.StartAsync(host.ConnectionString, ct);

        var received = new List<ChangeNotification>();
        using var subscription = replica.Notifier.Subscribe((n, _) =>
        {
            received.Add(n);
            return ValueTask.CompletedTask;
        });

        var sent = new ChangeNotification(Guid.NewGuid(), "Config", "self-heard-config", DateTimeOffset.UtcNow);
        await replica.Notifier.NotifyAsync(sent, ct);

        await PollUntilAsync(() => received.Count > 0, ct);

        received.Should().ContainSingle().Which.Should().Be(sent);
    }

    [Fact]
    public async Task Three_replicas_all_observe_a_single_notification()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await using var replicaA = await SimulatedReplica.StartAsync(host.ConnectionString, ct);
        await using var replicaB = await SimulatedReplica.StartAsync(host.ConnectionString, ct);
        await using var replicaC = await SimulatedReplica.StartAsync(host.ConnectionString, ct);

        var receivedB = new List<ChangeNotification>();
        var receivedC = new List<ChangeNotification>();
        using var subB = replicaB.Notifier.Subscribe((n, _) => { receivedB.Add(n); return ValueTask.CompletedTask; });
        using var subC = replicaC.Notifier.Subscribe((n, _) => { receivedC.Add(n); return ValueTask.CompletedTask; });

        var sent = new ChangeNotification(null, "Segment", "broadcast-segment", DateTimeOffset.UtcNow);
        await replicaA.Notifier.NotifyAsync(sent, ct);

        await PollUntilAsync(() => receivedB.Count > 0 && receivedC.Count > 0, ct);

        receivedB.Should().ContainSingle().Which.Should().Be(sent);
        receivedC.Should().ContainSingle().Which.Should().Be(sent);
    }

    [Fact]
    public async Task Malformed_document_on_the_stream_is_skipped_without_stopping_later_delivery()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await using var replicaA = await SimulatedReplica.StartAsync(host.ConnectionString, ct);
        await using var replicaB = await SimulatedReplica.StartAsync(host.ConnectionString, ct);

        var received = new List<ChangeNotification>();
        using var subscription = replicaB.Notifier.Subscribe((n, _) =>
        {
            received.Add(n);
            return ValueTask.CompletedTask;
        });

        // Insert a document with no "payload" field directly, bypassing
        // MongoChangeNotifier -- simulates a foreign/corrupt write landing on
        // the signal collection.
        await replicaA.ChangeNotifications.InsertOneAsync(new BsonDocument { { "not_payload", "oops" } }, cancellationToken: ct);

        var sent = new ChangeNotification(Guid.NewGuid(), "Flag", "after-malformed", DateTimeOffset.UtcNow);
        await replicaA.Notifier.NotifyAsync(sent, ct);

        await PollUntilAsync(() => received.Count > 0, ct);

        // Only the valid notification arrived -- the malformed document was
        // skipped, not delivered, and did not crash the listener.
        received.Should().ContainSingle().Which.Should().Be(sent);
    }

    [Fact]
    public async Task Listener_reconnects_after_its_change_stream_operation_is_killed()
    {
        // Forces the reconnect/backoff path: kill the listener's own
        // getMore operation server-side (a stand-in for a network blip or
        // the replica set stepping down) and prove it notices, reconnects,
        // and resumes delivering notifications. The Mongo equivalent of the
        // Postgres provider's own pg_terminate_backend-based reconnect test.
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await using var replicaA = await SimulatedReplica.StartAsync(host.ConnectionString, ct);
        await using var replicaB = await SimulatedReplica.StartAsync(host.ConnectionString, ct);

        var received = new List<ChangeNotification>();
        using var subscription = replicaB.Notifier.Subscribe((n, _) =>
        {
            received.Add(n);
            return ValueTask.CompletedTask;
        });

        await KillChangeStreamOperationsAsync(host.ConnectionString, ct);

        // The reconnect isn't independently observable from here, and the
        // kill itself can race a notification sent right after it, so retry
        // the publish until it lands rather than sending exactly once.
        var sent = new ChangeNotification(Guid.NewGuid(), "Flag", "post-reconnect-flag", DateTimeOffset.UtcNow);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        while (received.Count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await replicaA.Notifier.NotifyAsync(sent, ct);
            await Task.Delay(500, ct);
        }

        received.Should().ContainSingle("the listener should have reconnected and resumed delivery")
            .Which.Should().Be(sent);
    }

    /// <summary>
    /// Finds every in-progress <c>getMore</c> operation against the
    /// <c>changeNotifications</c> collection (there are two: one per
    /// replica's Change Stream cursor) and kills them server-side.
    /// </summary>
    private static async Task KillChangeStreamOperationsAsync(string connectionString, CancellationToken ct)
    {
        using var client = new MongoClient(connectionString);
        var admin = client.GetDatabase("admin");

        var result = await admin.RunCommandAsync<BsonDocument>(new BsonDocument { { "currentOp", 1 } }, cancellationToken: ct)
            .ConfigureAwait(false);

        foreach (var document in result["inprog"].AsBsonArray.Select(op => op.AsBsonDocument))
        {
            var isGetMore = document.TryGetValue("op", out var opType) && opType.AsString == "getmore";
            var targetsChangeNotifications = document.TryGetValue("ns", out var ns)
                && ns.AsString.EndsWith(MongoCollectionNames.ChangeNotifications, StringComparison.Ordinal);

            if (isGetMore && targetsChangeNotifications && document.TryGetValue("opid", out var opId))
            {
                await admin.RunCommandAsync<BsonDocument>(
                    new BsonDocument { { "killOp", 1 }, { "op", opId } }, cancellationToken: ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task PollUntilAsync(Func<bool> condition, CancellationToken ct, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100, ct);
        }
        condition().Should().BeTrue("the notification should have arrived within the timeout");
    }

    /// <summary>
    /// Stands in for one server replica: its own <see cref="IMongoClient"/>,
    /// <see cref="MongoChangeNotifier"/>, and <see cref="MongoChangeListenerHostedService"/>
    /// against a shared connection string, mirroring how
    /// <c>AddFeatlyMongoStore()</c> wires the same three pieces together in a
    /// real host.
    /// </summary>
    private sealed class SimulatedReplica : IAsyncDisposable
    {
        private readonly IMongoClient _client;
        private readonly MongoChangeListenerHostedService _listener;

        private SimulatedReplica(IMongoClient client, MongoChangeNotifier notifier, MongoChangeListenerHostedService listener, IMongoCollection<BsonDocument> changeNotifications)
        {
            _client = client;
            Notifier = notifier;
            _listener = listener;
            ChangeNotifications = changeNotifications;
        }

        public MongoChangeNotifier Notifier { get; }

        public IMongoCollection<BsonDocument> ChangeNotifications { get; }

        public static async Task<SimulatedReplica> StartAsync(string connectionString, CancellationToken ct)
        {
            MongoFeatlyDatabase.EnsureClassMapsRegistered();
            var mongoUrl = MongoUrl.Create(connectionString);
            var client = new MongoClient(connectionString);
            var mongoDatabase = client.GetDatabase(mongoUrl.DatabaseName);
            var database = new MongoFeatlyDatabase(mongoDatabase);

            var notifier = new MongoChangeNotifier(database);
            var listener = new MongoChangeListenerHostedService(database, notifier, NullLogger<MongoChangeListenerHostedService>.Instance);
            var changeNotifications = mongoDatabase.GetCollection<BsonDocument>(MongoCollectionNames.ChangeNotifications);

            await listener.StartAsync(ct).ConfigureAwait(false);
            // StartAsync returns once the loop is scheduled, not once the
            // Change Stream cursor is actually open -- a notification raised
            // before that point could be missed, so every test must wait for
            // this before publishing.
            await listener.ListeningAsync.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            return new SimulatedReplica(client, notifier, listener, changeNotifications);
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None).ConfigureAwait(false);
            _client.Dispose();
        }
    }
}
