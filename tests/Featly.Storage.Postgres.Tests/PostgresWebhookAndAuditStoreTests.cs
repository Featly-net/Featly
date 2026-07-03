using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trips for the webhook and audit stores, mirroring
/// <c>SqliteWebhookAndAuditStoreTests</c>: <c>PostgresWebhookStore</c> (with
/// the subscribed event-types collection), the persisted
/// <c>PostgresWebhookDeliveryStore</c> queue (due claim + result update), and
/// the append-only <c>PostgresAuditStore</c> log (filtered query with a
/// JsonElement payload).
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresWebhookAndAuditStoreTests
{
    [Fact]
    public async Task Webhook_endpoint_round_trips_event_types_and_is_deletable()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.WebhookStore;
        var env = Guid.NewGuid();

        var endpoint = new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            Name = "Slack relay",
            Url = "https://example.com/hooks/featly",
            Secret = "shhh",
            Enabled = true,
            EventTypes = [FeatlyEventTypes.FlagUpdated, FeatlyEventTypes.ChangeApplied],
            EnvironmentId = env,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await store.UpsertAsync(endpoint, ct);

        var loaded = await store.GetByIdAsync(endpoint.Id, ct);
        loaded.Should().NotBeNull();
        loaded!.Url.Should().Be("https://example.com/hooks/featly");
        loaded.Secret.Should().Be("shhh");
        loaded.EventTypes.Should().BeEquivalentTo([FeatlyEventTypes.FlagUpdated, FeatlyEventTypes.ChangeApplied]);
        loaded.EnvironmentId.Should().Be(env);

        (await store.ListAsync(ct)).Should().ContainSingle();

        await store.DeleteAsync(endpoint.Id, ct);
        (await store.GetByIdAsync(endpoint.Id, ct)).Should().BeNull();
    }

    [Fact]
    public async Task Delivery_queue_claims_due_pending_rows_then_marks_result()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.WebhookDeliveryStore;
        var endpointId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var dueNow = NewDelivery(endpointId, now.AddSeconds(-5));
        var future = NewDelivery(endpointId, now.AddMinutes(10));
        await store.EnqueueAsync([dueNow, future], ct);

        var due = await store.ListDueAsync(now, 10, ct);
        due.Should().ContainSingle().Which.Id.Should().Be(dueNow.Id);

        // Mark the due one succeeded — it drops out of the due set.
        dueNow.Status = WebhookDeliveryStatus.Succeeded;
        dueNow.AttemptCount = 1;
        dueNow.LastStatusCode = 200;
        dueNow.DeliveredAt = now;
        await store.UpdateAsync(dueNow, ct);

        (await store.ListDueAsync(now, 10, ct)).Should().BeEmpty();

        var reloaded = await store.GetByIdAsync(dueNow.Id, ct);
        reloaded!.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        reloaded.LastStatusCode.Should().Be(200);
        reloaded.DeliveredAt.Should().NotBeNull();

        (await store.ListByEndpointAsync(endpointId, 50, ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Audit_entries_round_trip_payload_and_filter()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.AuditStore;
        var env = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

        await store.AppendAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = t0,
            Action = FeatlyEventTypes.FlagUpdated,
            EntityType = "Flag",
            EntityKey = "checkout",
            EnvironmentId = env,
            ActorIdentifier = "kim@example.com",
            Data = JsonDocument.Parse("""{"before":{"enabled":false},"after":{"enabled":true}}""").RootElement,
        }, ct);
        await store.AppendAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = t0.AddMinutes(1),
            Action = FeatlyEventTypes.ConfigCreated,
            EntityType = "Config",
            EntityKey = "timeout",
            EnvironmentId = env,
            ActorIdentifier = "leo@example.com",
        }, ct);

        // Newest first, unfiltered.
        var all = await store.QueryAsync(ct: ct);
        all.Should().HaveCount(2);
        all[0].EntityType.Should().Be("Config"); // newer

        // Filter by entity type.
        var flags = await store.QueryAsync(entityType: "Flag", ct: ct);
        flags.Should().ContainSingle();
        flags[0].Data!.Value.GetProperty("after").GetProperty("enabled").GetBoolean().Should().BeTrue();

        // Filter by actor.
        (await store.QueryAsync(actorIdentifier: "leo@example.com", ct: ct)).Should().ContainSingle();

        // Date-range filter excludes the later row.
        (await store.QueryAsync(to: t0.AddSeconds(30), ct: ct))
            .Should().ContainSingle().Which.EntityType.Should().Be("Flag");
    }

    [Fact]
    public async Task Prune_removes_entries_older_than_the_cutoff()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.AuditStore;
        var now = DateTimeOffset.UtcNow;

        await store.AppendAsync(new AuditEntry { Id = Guid.NewGuid(), At = now.AddDays(-40), Action = "flag.updated", EntityType = "Flag" }, ct);
        await store.AppendAsync(new AuditEntry { Id = Guid.NewGuid(), At = now.AddDays(-1), Action = "flag.updated", EntityType = "Flag" }, ct);

        var removed = await store.PruneOlderThanAsync(now.AddDays(-30), ct);
        removed.Should().Be(1);

        var remaining = await store.QueryAsync(ct: ct);
        remaining.Should().ContainSingle();
        remaining[0].At.Should().BeCloseTo(now.AddDays(-1), TimeSpan.FromSeconds(2));
    }

    private static WebhookDelivery NewDelivery(Guid endpointId, DateTimeOffset nextAttempt) => new()
    {
        Id = Guid.NewGuid(),
        WebhookEndpointId = endpointId,
        EventType = FeatlyEventTypes.FlagUpdated,
        Payload = """{"type":"flag.updated"}""",
        Status = WebhookDeliveryStatus.Pending,
        NextAttemptAt = nextAttempt,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
