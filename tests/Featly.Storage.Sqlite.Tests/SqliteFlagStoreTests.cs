using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SqliteFlagStoreTests
{
    [Fact]
    public async Task Upsert_persists_flag_with_variants_and_tags()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var flag = NewBooleanFlag(envId, "new-checkout-flow");
        await host.Store.Flags.UpsertAsync(envId, flag, actor: "test", ct);

        var loaded = await host.Store.Flags.GetAsync(envId, "new-checkout-flow", ct);

        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("new-checkout-flow");
        loaded.Enabled.Should().BeFalse();
        loaded.DefaultVariantKey.Should().Be("off");
        loaded.Tags.Should().BeEquivalentTo(["checkout", "experiment"]);
        loaded.Variants.Should().HaveCount(2);
        loaded.Variants.Single(v => v.Key == "on").Value.GetBoolean().Should().BeTrue();
        loaded.Variants.Single(v => v.Key == "off").Value.GetBoolean().Should().BeFalse();
        loaded.UpdatedBy.Should().Be("test");
    }

    [Fact]
    public async Task Upsert_updates_existing_flag_keeping_id_and_createdAt()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var first = NewBooleanFlag(envId, "feature-x");
        await host.Store.Flags.UpsertAsync(envId, first, actor: "alice", ct);
        var originallyCreated = (await host.Store.Flags.GetAsync(envId, "feature-x", ct))!.CreatedAt;
        var originalId = (await host.Store.Flags.GetAsync(envId, "feature-x", ct))!.Id;

        var update = NewBooleanFlag(envId, "feature-x");
        update.Enabled = true;
        update.DefaultVariantKey = "on";
        await host.Store.Flags.UpsertAsync(envId, update, actor: "bob", ct);

        var loaded = await host.Store.Flags.GetAsync(envId, "feature-x", ct);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(originalId);
        loaded.CreatedAt.Should().Be(originallyCreated);
        loaded.Enabled.Should().BeTrue();
        loaded.DefaultVariantKey.Should().Be("on");
        loaded.UpdatedBy.Should().Be("bob");
    }

    [Fact]
    public async Task ListAsync_returns_only_non_archived_flags_for_environment()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        await host.Store.Flags.UpsertAsync(envA, NewBooleanFlag(envA, "alpha"), "t", ct);
        await host.Store.Flags.UpsertAsync(envA, NewBooleanFlag(envA, "beta"), "t", ct);
        await host.Store.Flags.UpsertAsync(envB, NewBooleanFlag(envB, "gamma"), "t", ct);
        await host.Store.Flags.ArchiveAsync(envA, "beta", "t", ct);

        var list = await host.Store.Flags.ListAsync(envA, ct);

        list.Should().HaveCount(1);
        list[0].Key.Should().Be("alpha");
    }

    [Fact]
    public async Task GetMostRecentUpdateAsync_returns_null_when_empty_then_tracks_updates()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var initial = await host.Store.Flags.GetMostRecentUpdateAsync(envId, ct);
        initial.Should().BeNull();

        await host.Store.Flags.UpsertAsync(envId, NewBooleanFlag(envId, "first"), "t", ct);
        var afterFirst = await host.Store.Flags.GetMostRecentUpdateAsync(envId, ct);
        afterFirst.Should().NotBeNull();

        await Task.Delay(10, ct);
        await host.Store.Flags.UpsertAsync(envId, NewBooleanFlag(envId, "second"), "t", ct);
        var afterSecond = await host.Store.Flags.GetMostRecentUpdateAsync(envId, ct);
        afterSecond.Should().NotBeNull();
        afterSecond!.Value.Should().BeOnOrAfter(afterFirst!.Value);
    }

    private static Flag NewBooleanFlag(Guid environmentId, string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Type = FlagType.Boolean,
        Enabled = false,
        DefaultVariantKey = "off",
        EnvironmentId = environmentId,
        Tags = ["checkout", "experiment"],
        Variants =
        [
            new Variant { Key = "on",  Name = "On",  Value = JsonSerializer.SerializeToElement(true)  },
            new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
        ],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "test",
        UpdatedBy = "test",
    };

    [Fact]
    public async Task Upsert_persists_flag_with_targeting_rules()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var flag = NewBooleanFlag(envId, "rules-flag");
        flag.Rules =
        [
            // Rule 1: BR users get the new flow.
            new Rule
            {
                Order = 0,
                Name = "BR rollout",
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "user.country",
                        Operator = ConditionOperator.Equals,
                        Value = JsonSerializer.SerializeToElement("BR"),
                    },
                ],
                Outcome = new RuleOutcome { VariantKey = "on" },
            },
            // Rule 2: enterprise plan gets a 50/50 split.
            new Rule
            {
                Order = 1,
                Name = "Enterprise 50/50",
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "user.plan",
                        Operator = ConditionOperator.Equals,
                        Value = JsonSerializer.SerializeToElement("enterprise"),
                    },
                ],
                Outcome = new RuleOutcome
                {
                    Splits =
                    [
                        new Split { VariantKey = "on",  Weight = 50 },
                        new Split { VariantKey = "off", Weight = 50 },
                    ],
                },
            },
        ];

        await host.Store.Flags.UpsertAsync(envId, flag, actor: "test", ct);

        var loaded = await host.Store.Flags.GetAsync(envId, "rules-flag", ct);

        loaded.Should().NotBeNull();
        loaded!.Rules.Should().HaveCount(2);

        var brRule = loaded.Rules.Single(r => r.Order == 0);
        brRule.Name.Should().Be("BR rollout");
        brRule.Enabled.Should().BeTrue();
        brRule.Conditions.Should().ContainSingle()
            .Which.Attribute.Should().Be("user.country");
        brRule.Outcome.VariantKey.Should().Be("on");
        brRule.Outcome.Splits.Should().BeNullOrEmpty();

        var enterpriseRule = loaded.Rules.Single(r => r.Order == 1);
        enterpriseRule.Outcome.VariantKey.Should().BeNullOrEmpty();
        enterpriseRule.Outcome.Splits.Should().HaveCount(2);
        enterpriseRule.Outcome.Splits!.Sum(s => s.Weight).Should().Be(100);
    }
}
