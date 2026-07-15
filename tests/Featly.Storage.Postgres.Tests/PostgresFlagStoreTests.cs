using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trip coverage for <c>PostgresFlagStore</c>, mirroring
/// <c>SqliteFlagStoreTests</c>. The rules/variants round-trip is the highest-risk
/// part of the Postgres mapping: <c>Flag.Variants</c>/<c>Flag.Rules</c> are
/// owned collections persisted as <c>jsonb</c> (vs. the SQLite provider's
/// raw-JSON-text column) — this proves the mapping holds for nested
/// conditions, weighted splits, and a boolean JsonElement value.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresFlagStoreTests
{
    [Fact]
    public async Task Upsert_persists_flag_with_variants_and_tags()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.FlagStore;

        var flag = NewBooleanFlag(envId, "new-checkout-flow");
        await store.UpsertAsync(envId, flag, actor: "test", ct);

        var loaded = await store.GetAsync(envId, "new-checkout-flow", ct);

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
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.FlagStore;

        var first = NewBooleanFlag(envId, "feature-x");
        await store.UpsertAsync(envId, first, actor: "alice", ct);
        var originallyCreated = (await store.GetAsync(envId, "feature-x", ct))!.CreatedAt;
        var originalId = (await store.GetAsync(envId, "feature-x", ct))!.Id;

        var update = NewBooleanFlag(envId, "feature-x");
        update.Enabled = true;
        update.DefaultVariantKey = "on";
        await store.UpsertAsync(envId, update, actor: "bob", ct);

        var loaded = await store.GetAsync(envId, "feature-x", ct);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(originalId);
        loaded.CreatedAt.Should().Be(originallyCreated);
        loaded.Enabled.Should().BeTrue();
        loaded.DefaultVariantKey.Should().Be("on");
        loaded.UpdatedBy.Should().Be("bob");
    }

    [Fact]
    public async Task Upsert_persists_flag_with_targeting_rules()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.FlagStore;

        var flag = NewBooleanFlag(envId, "rules-flag");
        flag.Rules =
        [
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
                        new Split { VariantKey = "on", Weight = 50 },
                        new Split { VariantKey = "off", Weight = 50 },
                    ],
                },
            },
        ];

        await store.UpsertAsync(envId, flag, actor: "test", ct);

        var loaded = await store.GetAsync(envId, "rules-flag", ct);

        loaded.Should().NotBeNull();
        loaded!.Rules.Should().HaveCount(2);
        var rule0 = loaded.Rules.Single(r => r.Order == 0);
        rule0.Name.Should().Be("BR rollout");
        rule0.Conditions.Single().Attribute.Should().Be("user.country");
        rule0.Conditions.Single().Value.GetString().Should().Be("BR");
        rule0.Outcome.VariantKey.Should().Be("on");

        var rule1 = loaded.Rules.Single(r => r.Order == 1);
        rule1.Outcome.Splits.Should().HaveCount(2);
        rule1.Outcome.Splits!.Single(s => s.VariantKey == "on").Weight.Should().Be(50);
    }

    [Fact]
    public async Task ListAsync_returns_only_non_archived_flags_for_environment()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();
        var store = host.FlagStore;

        await store.UpsertAsync(envA, NewBooleanFlag(envA, "alpha"), "t", ct);
        await store.UpsertAsync(envA, NewBooleanFlag(envA, "beta"), "t", ct);
        await store.UpsertAsync(envB, NewBooleanFlag(envB, "gamma"), "t", ct);
        await store.ArchiveAsync(envA, "beta", "t", ct);

        var list = await store.ListAsync(envA, ct);
        list.Should().HaveCount(1);
        list[0].Key.Should().Be("alpha");

        var archived = await store.ListArchivedAsync(envA, ct);
        archived.Should().HaveCount(1);
        archived[0].Key.Should().Be("beta");
    }

    [Fact]
    public async Task Archive_then_unarchive_round_trips()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.FlagStore;

        await store.UpsertAsync(envId, NewBooleanFlag(envId, "toggle-me"), "t", ct);
        await store.ArchiveAsync(envId, "toggle-me", "t", ct);
        (await store.GetAsync(envId, "toggle-me", ct))!.Archived.Should().BeTrue();

        await store.UnarchiveAsync(envId, "toggle-me", "t", ct);
        (await store.GetAsync(envId, "toggle-me", ct))!.Archived.Should().BeFalse();
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
            new Variant { Key = "on", Name = "On", Value = JsonSerializer.SerializeToElement(true) },
            new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
        ],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "test",
        UpdatedBy = "test",
    };
}
