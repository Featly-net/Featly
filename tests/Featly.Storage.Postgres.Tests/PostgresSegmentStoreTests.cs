using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trip coverage for <c>PostgresSegmentStore</c>, mirroring
/// <c>SqliteSegmentStoreTests</c>. <c>Segment.Conditions</c> is an owned
/// collection persisted as <c>jsonb</c> — this proves the mapping holds for
/// nested condition operators (Equals, In) and array values.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresSegmentStoreTests
{
    private static readonly string[] EnterpriseCountries = ["US", "CA"];

    [Fact]
    public async Task Upsert_then_get_round_trips_conditions()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SegmentStore;
        var envId = Guid.NewGuid();

        var segment = NewSegment(envId, "enterprise-customers");
        await store.UpsertAsync(envId, segment, actor: "test", ct);

        var loaded = await store.GetAsync(envId, "enterprise-customers", ct);

        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("enterprise-customers");
        loaded.Name.Should().Be("Enterprise customers");
        loaded.Conditions.Should().HaveCount(2);

        var planCondition = loaded.Conditions.Single(c => c.Attribute == "user.plan");
        planCondition.Operator.Should().Be(ConditionOperator.Equals);
        planCondition.Value.GetString().Should().Be("enterprise");
        planCondition.Negate.Should().BeFalse();

        var countryCondition = loaded.Conditions.Single(c => c.Attribute == "user.country");
        countryCondition.Operator.Should().Be(ConditionOperator.In);
        countryCondition.Value.EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["US", "CA"]);
    }

    [Fact]
    public async Task List_returns_all_segments_in_the_environment_ordered_by_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SegmentStore;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        await store.UpsertAsync(envA, NewSegment(envA, "zulus"), "t", ct);
        await store.UpsertAsync(envA, NewSegment(envA, "alphas"), "t", ct);
        await store.UpsertAsync(envB, NewSegment(envB, "betas"), "t", ct);

        var list = await store.ListAsync(envA, ct);

        list.Should().HaveCount(2);
        list.Select(s => s.Key).Should().ContainInOrder("alphas", "zulus");
    }

    [Fact]
    public async Task Upsert_overwrites_existing_segment_conditions()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SegmentStore;
        var envId = Guid.NewGuid();

        var first = NewSegment(envId, "beta-testers");
        await store.UpsertAsync(envId, first, "alice", ct);

        var update = NewSegment(envId, "beta-testers");
        update.Name = "Beta testers (updated)";
        update.Conditions =
        [
            new Condition
            {
                Attribute = "user.beta",
                Operator = ConditionOperator.Equals,
                Value = JsonSerializer.SerializeToElement(true),
            },
        ];
        await store.UpsertAsync(envId, update, "bob", ct);

        var loaded = await store.GetAsync(envId, "beta-testers", ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Beta testers (updated)");
        loaded.Conditions.Should().HaveCount(1);
        loaded.Conditions[0].Attribute.Should().Be("user.beta");
        loaded.UpdatedBy.Should().Be("bob");
    }

    [Fact]
    public async Task Delete_removes_segment_and_is_idempotent()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SegmentStore;
        var envId = Guid.NewGuid();

        await store.UpsertAsync(envId, NewSegment(envId, "to-delete"), "t", ct);
        await store.DeleteAsync(envId, "to-delete", "t", ct);

        var loaded = await store.GetAsync(envId, "to-delete", ct);
        loaded.Should().BeNull();

        var deleteAgain = async () => await store.DeleteAsync(envId, "to-delete", "t", ct);
        await deleteAgain.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Archive_excludes_segment_from_list_and_persists_flag()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SegmentStore;
        var envId = Guid.NewGuid();

        await store.UpsertAsync(envId, NewSegment(envId, "legacy"), "t", ct);
        await store.ArchiveAsync(envId, "legacy", "archiver", ct);

        var active = await store.ListAsync(envId, ct);
        active.Should().NotContain(s => s.Key == "legacy");

        var archived = await store.ListArchivedAsync(envId, ct);
        archived.Should().ContainSingle(s => s.Key == "legacy");

        var loaded = await store.GetAsync(envId, "legacy", ct);
        loaded!.Archived.Should().BeTrue();
        loaded.UpdatedBy.Should().Be("archiver");
    }

    [Fact]
    public async Task Unarchive_restores_segment_to_list()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.SegmentStore;
        var envId = Guid.NewGuid();

        await store.UpsertAsync(envId, NewSegment(envId, "legacy"), "t", ct);
        await store.ArchiveAsync(envId, "legacy", "t", ct);
        await store.UnarchiveAsync(envId, "legacy", "restorer", ct);

        var active = await store.ListAsync(envId, ct);
        active.Should().ContainSingle(s => s.Key == "legacy");

        var loaded = await store.GetAsync(envId, "legacy", ct);
        loaded!.Archived.Should().BeFalse();
        loaded.UpdatedBy.Should().Be("restorer");
    }

    private static Segment NewSegment(Guid envId, string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = "Enterprise customers",
        EnvironmentId = envId,
        Conditions =
        [
            new Condition
            {
                Attribute = "user.plan",
                Operator = ConditionOperator.Equals,
                Value = JsonSerializer.SerializeToElement("enterprise"),
            },
            new Condition
            {
                Attribute = "user.country",
                Operator = ConditionOperator.In,
                Value = JsonSerializer.SerializeToElement(EnterpriseCountries),
            },
        ],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "test",
        UpdatedBy = "test",
    };
}
