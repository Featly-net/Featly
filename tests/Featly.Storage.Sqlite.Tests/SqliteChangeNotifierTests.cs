using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SqliteChangeNotifierTests
{
    [Fact]
    public async Task Subscribe_receives_published_notifications()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var notifier = host.Store.Changes;

        var received = new List<ChangeNotification>();
        using var subscription = notifier.Subscribe((n, _) =>
        {
            received.Add(n);
            return ValueTask.CompletedTask;
        });

        var envId = Guid.NewGuid();
        await notifier.NotifyAsync(new ChangeNotification(envId, "Flag", "x", DateTimeOffset.UtcNow), ct);

        received.Should().HaveCount(1);
        received[0].EntityType.Should().Be("Flag");
        received[0].EnvironmentId.Should().Be(envId);
    }

    [Fact]
    public async Task Dispose_unsubscribes_the_handler()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var notifier = host.Store.Changes;

        var hits = 0;
        // `using` so the subscription is disposed even if NotifyAsync throws.
        // We still call Dispose explicitly below to assert the unsubscribe
        // behavior, which is idempotent thanks to the Interlocked guard.
        using var subscription = notifier.Subscribe((_, _) => { hits++; return ValueTask.CompletedTask; });
        await notifier.NotifyAsync(new ChangeNotification(null, "Flag", null, DateTimeOffset.UtcNow), ct);
        subscription.Dispose();
        await notifier.NotifyAsync(new ChangeNotification(null, "Flag", null, DateTimeOffset.UtcNow), ct);

        hits.Should().Be(1);
    }
}
