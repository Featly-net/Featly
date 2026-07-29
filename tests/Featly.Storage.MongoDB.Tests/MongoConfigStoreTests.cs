using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.MongoDB.Tests;

[Trait("Category", "RequiresMongoDB")]
public class MongoConfigStoreTests
{
    [Fact]
    public async Task Upsert_persists_config_with_default_value_and_type()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.ConfigStore;

        var config = NewStringConfig(envId, "welcome-message");
        await store.UpsertAsync(envId, config, actor: "test", ct);

        var loaded = await store.GetAsync(envId, "welcome-message", ct);

        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("welcome-message");
        loaded.Type.Should().Be(ConfigType.String);
        loaded.DefaultValue.GetString().Should().Be("hello");
        loaded.Tags.Should().BeEquivalentTo(["onboarding"]);
        loaded.UpdatedBy.Should().Be("test");
    }

    [Fact]
    public async Task Upsert_updates_existing_config_keeping_id_and_createdAt()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.ConfigStore;

        var first = NewStringConfig(envId, "max-retries");
        await store.UpsertAsync(envId, first, actor: "alice", ct);
        var original = (await store.GetAsync(envId, "max-retries", ct))!;

        var update = NewStringConfig(envId, "max-retries");
        update.DefaultValue = JsonSerializer.SerializeToElement("updated");
        await store.UpsertAsync(envId, update, actor: "bob", ct);

        var loaded = await store.GetAsync(envId, "max-retries", ct);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(original.Id);
        loaded.CreatedAt.Should().Be(original.CreatedAt);
        loaded.DefaultValue.GetString().Should().Be("updated");
        loaded.UpdatedBy.Should().Be("bob");
    }

    [Fact]
    public async Task Upsert_persists_config_with_targeting_rules()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.ConfigStore;

        var config = NewStringConfig(envId, "rules-config");
        config.Rules =
        [
            new ConfigRule
            {
                Order = 0,
                Name = "BR override",
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "user.country",
                        Operator = ConditionOperator.Equals,
                        Value = JsonSerializer.SerializeToElement("BR"),
                    },
                ],
                Value = JsonSerializer.SerializeToElement("ola"),
            },
        ];

        await store.UpsertAsync(envId, config, actor: "test", ct);

        var loaded = await store.GetAsync(envId, "rules-config", ct);
        loaded.Should().NotBeNull();
        loaded!.Rules.Should().HaveCount(1);
        var rule = loaded.Rules.Single();
        rule.Name.Should().Be("BR override");
        rule.Conditions.Single().Value.GetString().Should().Be("BR");
        rule.Value.GetString().Should().Be("ola");
        rule.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_returns_only_non_archived_configs_ordered_by_key()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.ConfigStore;

        await store.UpsertAsync(envId, NewStringConfig(envId, "zeta"), "t", ct);
        await store.UpsertAsync(envId, NewStringConfig(envId, "alpha"), "t", ct);
        await store.UpsertAsync(envId, NewStringConfig(envId, "beta"), "t", ct);
        await store.ArchiveAsync(envId, "beta", "t", ct);

        var list = await store.ListAsync(envId, ct);
        list.Select(c => c.Key).Should().Equal("alpha", "zeta");

        var archived = await store.ListArchivedAsync(envId, ct);
        archived.Select(c => c.Key).Should().Equal("beta");
    }

    [Fact]
    public async Task Archive_then_unarchive_round_trips()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();
        var store = host.ConfigStore;

        await store.UpsertAsync(envId, NewStringConfig(envId, "toggle-me"), "t", ct);
        await store.ArchiveAsync(envId, "toggle-me", "t", ct);
        (await store.GetAsync(envId, "toggle-me", ct))!.Archived.Should().BeTrue();

        await store.UnarchiveAsync(envId, "toggle-me", "t", ct);
        (await store.GetAsync(envId, "toggle-me", ct))!.Archived.Should().BeFalse();
    }

    private static Config NewStringConfig(Guid environmentId, string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Type = ConfigType.String,
        DefaultValue = JsonSerializer.SerializeToElement("hello"),
        EnvironmentId = environmentId,
        Tags = ["onboarding"],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "test",
        UpdatedBy = "test",
    };
}
