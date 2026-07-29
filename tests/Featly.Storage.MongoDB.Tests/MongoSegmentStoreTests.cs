using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.MongoDB.Tests;

[Trait("Category", "RequiresMongoDB")]
public class MongoSegmentStoreTests
{
    [Fact]
    public async Task Upsert_persists_segment_with_conditions()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.SegmentStore;

        var segment = NewSegment(envId, "beta-users");
        await store.UpsertAsync(envId, segment, actor: "test", ct);

        var loaded = await store.GetAsync(envId, "beta-users", ct);

        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("beta-users");
        loaded.Conditions.Should().HaveCount(1);
        loaded.Conditions[0].Attribute.Should().Be("user.plan");
        loaded.Conditions[0].Value.GetString().Should().Be("beta");
        loaded.UpdatedBy.Should().Be("test");
    }

    [Fact]
    public async Task Upsert_updates_existing_segment_keeping_id_and_createdAt()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.SegmentStore;

        var first = NewSegment(envId, "internal");
        await store.UpsertAsync(envId, first, actor: "alice", ct);
        var original = (await store.GetAsync(envId, "internal", ct))!;

        var update = NewSegment(envId, "internal");
        update.Name = "Internal Testers Renamed";
        await store.UpsertAsync(envId, update, actor: "bob", ct);

        var loaded = await store.GetAsync(envId, "internal", ct);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(original.Id);
        loaded.CreatedAt.Should().Be(original.CreatedAt);
        loaded.Name.Should().Be("Internal Testers Renamed");
        loaded.UpdatedBy.Should().Be("bob");
    }

    [Fact]
    public async Task Same_key_is_allowed_across_different_environments()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();
        var store = host.SegmentStore;

        await store.UpsertAsync(envA, NewSegment(envA, "dev"), "t", ct);
        await store.UpsertAsync(envB, NewSegment(envB, "dev"), "t", ct);

        (await store.GetAsync(envA, "dev", ct)).Should().NotBeNull();
        (await store.GetAsync(envB, "dev", ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task ListAsync_returns_only_non_archived_segments_ordered_by_key()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.SegmentStore;

        await store.UpsertAsync(envId, NewSegment(envId, "zeta"), "t", ct);
        await store.UpsertAsync(envId, NewSegment(envId, "alpha"), "t", ct);
        await store.UpsertAsync(envId, NewSegment(envId, "beta"), "t", ct);
        await store.ArchiveAsync(envId, "beta", "t", ct);

        var list = await store.ListAsync(envId, ct);
        list.Select(s => s.Key).Should().Equal("alpha", "zeta");

        var archived = await store.ListArchivedAsync(envId, ct);
        archived.Select(s => s.Key).Should().Equal("beta");
    }

    [Fact]
    public async Task Archive_then_unarchive_round_trips()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.SegmentStore;

        await store.UpsertAsync(envId, NewSegment(envId, "toggle-me"), "t", ct);
        await store.ArchiveAsync(envId, "toggle-me", "t", ct);
        (await store.GetAsync(envId, "toggle-me", ct))!.Archived.Should().BeTrue();

        await store.UnarchiveAsync(envId, "toggle-me", "t", ct);
        (await store.GetAsync(envId, "toggle-me", ct))!.Archived.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_row_and_is_idempotent()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.SegmentStore;

        await store.UpsertAsync(envId, NewSegment(envId, "temp"), "t", ct);
        await store.DeleteAsync(envId, "temp", "t", ct);
        (await store.GetAsync(envId, "temp", ct)).Should().BeNull();

        // Idempotent: deleting again does not throw.
        await store.DeleteAsync(envId, "temp", "t", ct);
    }

    private static Segment NewSegment(Guid environmentId, string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        EnvironmentId = environmentId,
        Conditions =
        [
            new Condition
            {
                Attribute = "user.plan",
                Operator = ConditionOperator.Equals,
                Value = JsonSerializer.SerializeToElement("beta"),
            },
        ],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "test",
        UpdatedBy = "test",
    };
}
