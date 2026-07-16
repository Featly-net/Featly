using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Covers cross-replica change push (ADR-0026, issue #258): several
/// <see cref="PostgresChangeNotifier"/> + <see cref="PostgresChangeListenerHostedService"/>
/// pairs, each standing in for a server replica, against the same real
/// Postgres database. A notification raised through one must be observed by
/// every other.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresChangeNotifierTests
{
    [Fact]
    public async Task Notification_raised_on_one_replica_is_observed_by_another()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
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
        // exclusively through the LISTEN round-trip, so the originating replica
        // must hear its own change back the same way every other replica does.
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
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
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
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

    private static async Task PollUntilAsync(Func<bool> condition, CancellationToken ct, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100, ct);
        }
        condition().Should().BeTrue("the notification should have arrived within the timeout");
    }

    /// <summary>
    /// Stands in for one server replica: its own <see cref="IDbContextFactory{TContext}"/>,
    /// <see cref="PostgresChangeNotifier"/>, and <see cref="PostgresChangeListenerHostedService"/>
    /// against a shared connection string, mirroring how <c>AddFeatlyPostgresStore()</c>
    /// wires the same three pieces together in a real host.
    /// </summary>
    private sealed class SimulatedReplica : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly PostgresChangeListenerHostedService _listener;

        private SimulatedReplica(ServiceProvider provider, PostgresChangeNotifier notifier, PostgresChangeListenerHostedService listener)
        {
            _provider = provider;
            Notifier = notifier;
            _listener = listener;
        }

        public PostgresChangeNotifier Notifier { get; }

        public static async Task<SimulatedReplica> StartAsync(string connectionString, CancellationToken ct)
        {
            var services = new ServiceCollection();
            services.AddDbContextFactory<FeatlyDbContext>(b => b.UseNpgsql(connectionString));
            var provider = services.BuildServiceProvider();

            var factory = provider.GetRequiredService<IDbContextFactory<FeatlyDbContext>>();
            var notifier = new PostgresChangeNotifier(factory);
            var options = Options.Create(new PostgresFeatlyStoreOptions { ConnectionString = connectionString });
            var listener = new PostgresChangeListenerHostedService(options, notifier, NullLogger<PostgresChangeListenerHostedService>.Instance);

            await listener.StartAsync(ct).ConfigureAwait(false);
            // StartAsync returns once the loop is scheduled, not once LISTEN has
            // actually been issued -- a NOTIFY sent before that point is lost
            // forever (Postgres does not queue it for a not-yet-listening
            // session), so every test must wait for this before publishing.
            await listener.ListeningAsync.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            return new SimulatedReplica(provider, notifier, listener);
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
