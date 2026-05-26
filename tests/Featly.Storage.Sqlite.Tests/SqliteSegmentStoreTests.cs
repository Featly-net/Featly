using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SqliteSegmentStoreTests
{
    // CA1861 — array literals at call sites get re-allocated on every invocation;
    // hoist the small fixtures to static readonly fields.
    private static readonly string[] EnterpriseCountries = ["US", "CA"];


    [Fact]
    public async Task Upsert_then_get_round_trips_conditions()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var segment = NewSegment(envId, "enterprise-customers");
        await host.Store.Segments.UpsertAsync(envId, segment, actor: "test", ct);

        var loaded = await host.Store.Segments.GetAsync(envId, "enterprise-customers", ct);

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
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        await host.Store.Segments.UpsertAsync(envA, NewSegment(envA, "zulus"), "t", ct);
        await host.Store.Segments.UpsertAsync(envA, NewSegment(envA, "alphas"), "t", ct);
        await host.Store.Segments.UpsertAsync(envB, NewSegment(envB, "betas"), "t", ct);

        var list = await host.Store.Segments.ListAsync(envA, ct);

        list.Should().HaveCount(2);
        list.Select(s => s.Key).Should().ContainInOrder("alphas", "zulus");
    }

    [Fact]
    public async Task Upsert_overwrites_existing_segment_conditions()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var first = NewSegment(envId, "beta-testers");
        await host.Store.Segments.UpsertAsync(envId, first, "alice", ct);

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
        await host.Store.Segments.UpsertAsync(envId, update, "bob", ct);

        var loaded = await host.Store.Segments.GetAsync(envId, "beta-testers", ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Beta testers (updated)");
        loaded.Conditions.Should().HaveCount(1);
        loaded.Conditions[0].Attribute.Should().Be("user.beta");
        loaded.UpdatedBy.Should().Be("bob");
    }

    [Fact]
    public async Task Delete_removes_segment_and_is_idempotent()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        await host.Store.Segments.UpsertAsync(envId, NewSegment(envId, "to-delete"), "t", ct);
        await host.Store.Segments.DeleteAsync(envId, "to-delete", "t", ct);

        var loaded = await host.Store.Segments.GetAsync(envId, "to-delete", ct);
        loaded.Should().BeNull();

        // Idempotent: deleting again must not throw.
        var deleteAgain = async () =>
            await host.Store.Segments.DeleteAsync(envId, "to-delete", "t", ct);
        await deleteAgain.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetMostRecentUpdate_tracks_updates()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var initial = await host.Store.Segments.GetMostRecentUpdateAsync(envId, ct);
        initial.Should().BeNull();

        await host.Store.Segments.UpsertAsync(envId, NewSegment(envId, "first"), "t", ct);
        var after = await host.Store.Segments.GetMostRecentUpdateAsync(envId, ct);
        after.Should().NotBeNull();
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
