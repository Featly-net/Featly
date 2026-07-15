using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Round-trips for the M10 stores: <c>WebhookEndpoint</c> (with the subscribed
/// event-types collection), the persisted <c>WebhookDelivery</c> queue (due
/// claim + result update), and the append-only <c>AuditEntry</c> log (filtered
/// query with a JsonElement payload).
/// </summary>
public class SqliteWebhookAndAuditStoreTests
{
    [Fact]
    public async Task Webhook_endpoint_round_trips_event_types_and_is_deletable()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
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
        await host.Store.Webhooks.UpsertAsync(endpoint, ct);

        var loaded = await host.Store.Webhooks.GetByIdAsync(endpoint.Id, ct);
        loaded.Should().NotBeNull();
        loaded!.Url.Should().Be("https://example.com/hooks/featly");
        loaded.Secret.Should().Be("shhh");
        loaded.EventTypes.Should().BeEquivalentTo([FeatlyEventTypes.FlagUpdated, FeatlyEventTypes.ChangeApplied]);
        loaded.EnvironmentId.Should().Be(env);

        (await host.Store.Webhooks.ListAsync(ct)).Should().ContainSingle();

        await host.Store.Webhooks.DeleteAsync(endpoint.Id, ct);
        (await host.Store.Webhooks.GetByIdAsync(endpoint.Id, ct)).Should().BeNull();
    }

    [Fact]
    public async Task Delivery_queue_claims_due_pending_rows_then_marks_result()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var endpointId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var dueNow = NewDelivery(endpointId, now.AddSeconds(-5));
        var future = NewDelivery(endpointId, now.AddMinutes(10));
        await host.Store.WebhookDeliveries.EnqueueAsync([dueNow, future], ct);

        var due = await host.Store.WebhookDeliveries.ListDueAsync(now, 10, ct);
        due.Should().ContainSingle().Which.Id.Should().Be(dueNow.Id);

        // Mark the due one succeeded — it drops out of the due set.
        dueNow.Status = WebhookDeliveryStatus.Succeeded;
        dueNow.AttemptCount = 1;
        dueNow.LastStatusCode = 200;
        dueNow.DeliveredAt = now;
        await host.Store.WebhookDeliveries.UpdateAsync(dueNow, ct);

        (await host.Store.WebhookDeliveries.ListDueAsync(now, 10, ct)).Should().BeEmpty();

        var reloaded = await host.Store.WebhookDeliveries.GetByIdAsync(dueNow.Id, ct);
        reloaded!.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        reloaded.LastStatusCode.Should().Be(200);
        reloaded.DeliveredAt.Should().NotBeNull();

        (await host.Store.WebhookDeliveries.ListByEndpointAsync(endpointId, 50, ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task TryClaimDue_leases_a_due_row_once_and_hides_it_from_a_second_claim()
    {
        // Multi-instance safety (issue #237): the first claim leases the row by
        // pushing NextAttemptAt forward, so a concurrent worker's due query and a
        // second claim both miss it. A not-yet-due row can never be claimed.
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var endpointId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var dueNow = NewDelivery(endpointId, now.AddSeconds(-5));
        var future = NewDelivery(endpointId, now.AddMinutes(10));
        await host.Store.WebhookDeliveries.EnqueueAsync([dueNow, future], ct);

        var leaseUntil = now.AddMinutes(1);
        (await host.Store.WebhookDeliveries.TryClaimDueAsync(dueNow.Id, now, leaseUntil, ct)).Should().BeTrue();
        (await host.Store.WebhookDeliveries.TryClaimDueAsync(dueNow.Id, now, leaseUntil, ct)).Should().BeFalse();
        (await host.Store.WebhookDeliveries.TryClaimDueAsync(future.Id, now, leaseUntil, ct)).Should().BeFalse();

        // The lease removed the row from the due set until leaseUntil.
        (await host.Store.WebhookDeliveries.ListDueAsync(now, 10, ct)).Should().BeEmpty();
        var leased = await host.Store.WebhookDeliveries.GetByIdAsync(dueNow.Id, ct);
        leased!.NextAttemptAt.Should().BeCloseTo(leaseUntil, TimeSpan.FromSeconds(1));
        leased.Status.Should().Be(WebhookDeliveryStatus.Pending);
    }

    [Fact]
    public async Task RecordCircuitState_persists_and_upsert_preserves_it()
    {
        // Issue #207: circuit-breaker fields round-trip and are worker-managed —
        // an admin UpsertAsync must not reset a tripped circuit.
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var endpoint = new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            Name = "hook",
            Url = "https://example.com/hook",
            Secret = "s",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await host.Store.Webhooks.UpsertAsync(endpoint, ct);

        var openUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        await host.Store.Webhooks.RecordCircuitStateAsync(endpoint.Id, 3, openUntil, ct);

        var tripped = await host.Store.Webhooks.GetByIdAsync(endpoint.Id, ct);
        tripped!.ConsecutiveFailures.Should().Be(3);
        tripped.CircuitOpenUntil.Should().BeCloseTo(openUntil, TimeSpan.FromSeconds(1));

        // An admin edit (rename) must not clear the tripped circuit.
        endpoint.Name = "renamed";
        await host.Store.Webhooks.UpsertAsync(endpoint, ct);
        var afterEdit = await host.Store.Webhooks.GetByIdAsync(endpoint.Id, ct);
        afterEdit!.Name.Should().Be("renamed");
        afterEdit.ConsecutiveFailures.Should().Be(3);
        afterEdit.CircuitOpenUntil.Should().BeCloseTo(openUntil, TimeSpan.FromSeconds(1));

        // A success resets it.
        await host.Store.Webhooks.RecordCircuitStateAsync(endpoint.Id, 0, null, ct);
        var closed = await host.Store.Webhooks.GetByIdAsync(endpoint.Id, ct);
        closed!.ConsecutiveFailures.Should().Be(0);
        closed.CircuitOpenUntil.Should().BeNull();
    }

    [Fact]
    public async Task Audit_entries_round_trip_payload_and_filter()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var env = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

        await host.Store.Audit.AppendAsync(new AuditEntry
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
        await host.Store.Audit.AppendAsync(new AuditEntry
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
        var all = await host.Store.Audit.QueryAsync(ct: ct);
        all.Should().HaveCount(2);
        all[0].EntityType.Should().Be("Config"); // newer

        // Filter by entity type.
        var flags = await host.Store.Audit.QueryAsync(entityType: "Flag", ct: ct);
        flags.Should().ContainSingle();
        flags[0].Data!.Value.GetProperty("after").GetProperty("enabled").GetBoolean().Should().BeTrue();

        // Filter by actor.
        (await host.Store.Audit.QueryAsync(actorIdentifier: "leo@example.com", ct: ct)).Should().ContainSingle();

        // Date-range filter excludes the later row.
        (await host.Store.Audit.QueryAsync(to: t0.AddSeconds(30), ct: ct))
            .Should().ContainSingle().Which.EntityType.Should().Be("Flag");
    }

    [Fact]
    public async Task Audit_append_builds_a_verifiable_hash_chain()
    {
        // Issue #208: appends form a linear, tamper-evident chain — monotonic
        // Sequence, each PreviousHash pointing at the prior entry's Hash.
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            await host.Store.Audit.AppendAsync(new AuditEntry
            {
                Id = Guid.NewGuid(),
                At = DateTimeOffset.UtcNow.AddSeconds(i),
                Action = FeatlyEventTypes.FlagUpdated,
                EntityType = "Flag",
                EntityKey = $"k{i}",
                Data = JsonDocument.Parse("""{"n":1}""").RootElement,
            }, ct);
        }

        var chain = (await host.Store.Audit.QueryAsync(limit: 100, ct: ct)).OrderBy(e => e.Sequence).ToList();
        chain.Select(e => e.Sequence).Should().Equal(1, 2, 3);
        chain[0].PreviousHash.Should().BeNull(); // genesis
        chain[1].PreviousHash.Should().Be(chain[0].Hash);
        chain[2].PreviousHash.Should().Be(chain[1].Hash);
        chain.Should().OnlyContain(e => e.Hash != null);

        var verdict = await host.Store.Audit.VerifyChainAsync(ct);
        verdict.IsIntact.Should().BeTrue();
        verdict.EntriesChecked.Should().Be(3);
    }

    [Fact]
    public async Task Audit_verifier_detects_modification_and_deletion()
    {
        // Build a real chain via the store, then tamper with the read-back list to
        // prove the verifier catches a modified field and a deleted middle entry.
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            await host.Store.Audit.AppendAsync(new AuditEntry
            {
                Id = Guid.NewGuid(),
                At = DateTimeOffset.UtcNow.AddSeconds(i),
                Action = FeatlyEventTypes.FlagUpdated,
                EntityType = "Flag",
                EntityKey = $"k{i}",
            }, ct);
        }

        var chain = (await host.Store.Audit.QueryAsync(limit: 100, ct: ct)).OrderBy(e => e.Sequence).ToList();
        AuditChainVerifier.Verify(chain).IsIntact.Should().BeTrue();

        // Modify a field without recomputing its Hash (content fields are init-only,
        // so rebuild the row with the tampered value but the original chain fields)
        // -> content mismatch detected at that entry.
        var tampered = new AuditEntry
        {
            Id = chain[1].Id,
            At = chain[1].At,
            Action = chain[1].Action,
            EntityType = chain[1].EntityType,
            EntityKey = chain[1].EntityKey,
            EnvironmentId = chain[1].EnvironmentId,
            ActorIdentifier = "attacker@example.com",
            Data = chain[1].Data,
            Sequence = chain[1].Sequence,
            PreviousHash = chain[1].PreviousHash,
            Hash = chain[1].Hash,
        };
        var modified = AuditChainVerifier.Verify([chain[0], tampered, chain[2]]);
        modified.IsIntact.Should().BeFalse();
        modified.BrokenAtSequence.Should().Be(chain[1].Sequence);
        modified.Detail.Should().Contain("modified");

        // Delete the middle entry -> broken previous-hash link at the next entry.
        var deleted = AuditChainVerifier.Verify([chain[0], chain[2]]);
        deleted.IsIntact.Should().BeFalse();
        deleted.BrokenAtSequence.Should().Be(chain[2].Sequence);
        deleted.Detail.Should().Contain("deleted");
    }

    [Fact]
    public async Task Prune_removes_entries_older_than_the_cutoff()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        await host.Store.Audit.AppendAsync(new AuditEntry { Id = Guid.NewGuid(), At = now.AddDays(-40), Action = "flag.updated", EntityType = "Flag" }, ct);
        await host.Store.Audit.AppendAsync(new AuditEntry { Id = Guid.NewGuid(), At = now.AddDays(-1), Action = "flag.updated", EntityType = "Flag" }, ct);

        var removed = await host.Store.Audit.PruneOlderThanAsync(now.AddDays(-30), ct);
        removed.Should().Be(1);

        var remaining = await host.Store.Audit.QueryAsync(ct: ct);
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
