using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SqliteConfigStoreTests
{
    [Fact]
    public async Task Upsert_persists_config_with_default_value_tags_and_rules()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var config = NewIntConfig(envId, "checkout.timeout");
        config.Rules =
        [
            new ConfigRule
            {
                Order = 0,
                Name = "Enterprise gets longer timeout",
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "user.plan",
                        Operator = ConditionOperator.Equals,
                        Value = JsonSerializer.SerializeToElement("enterprise"),
                    },
                ],
                Value = JsonSerializer.SerializeToElement(60),
            },
        ];

        await host.Store.Configs.UpsertAsync(envId, config, actor: "test", ct);

        var loaded = await host.Store.Configs.GetAsync(envId, "checkout.timeout", ct);

        loaded.Should().NotBeNull();
        loaded!.Type.Should().Be(ConfigType.Int);
        loaded.DefaultValue.GetInt32().Should().Be(30);
        loaded.Tags.Should().BeEquivalentTo(["checkout", "perf"]);
        loaded.Rules.Should().ContainSingle();
        loaded.Rules[0].Value.GetInt32().Should().Be(60);
        loaded.Rules[0].Conditions.Should().ContainSingle()
            .Which.Attribute.Should().Be("user.plan");
    }

    [Fact]
    public async Task Upsert_overwrites_existing_config_and_keeps_id()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var first = NewIntConfig(envId, "checkout.timeout");
        await host.Store.Configs.UpsertAsync(envId, first, "alice", ct);
        var originalId = (await host.Store.Configs.GetAsync(envId, "checkout.timeout", ct))!.Id;

        var update = NewIntConfig(envId, "checkout.timeout");
        update.DefaultValue = JsonSerializer.SerializeToElement(45);
        await host.Store.Configs.UpsertAsync(envId, update, "bob", ct);

        var loaded = await host.Store.Configs.GetAsync(envId, "checkout.timeout", ct);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(originalId);
        loaded.DefaultValue.GetInt32().Should().Be(45);
        loaded.UpdatedBy.Should().Be("bob");
    }

    [Fact]
    public async Task ListAsync_filters_archived_and_scopes_per_environment()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        await host.Store.Configs.UpsertAsync(envA, NewIntConfig(envA, "alpha"), "t", ct);
        await host.Store.Configs.UpsertAsync(envA, NewIntConfig(envA, "beta"), "t", ct);
        await host.Store.Configs.UpsertAsync(envB, NewIntConfig(envB, "gamma"), "t", ct);
        await host.Store.Configs.ArchiveAsync(envA, "beta", "t", ct);

        var listA = await host.Store.Configs.ListAsync(envA, ct);

        listA.Should().HaveCount(1);
        listA[0].Key.Should().Be("alpha");
    }

    [Theory]
    [InlineData(ConfigType.String, "\"hello\"")]
    [InlineData(ConfigType.Int, "42")]
    [InlineData(ConfigType.Long, "9223372036854775000")]
    [InlineData(ConfigType.Double, "3.14")]
    [InlineData(ConfigType.Bool, "true")]
    [InlineData(ConfigType.Json, "{\"feature\":\"flag\",\"version\":2}")]
    public async Task Round_trips_every_supported_config_type(ConfigType type, string rawJsonValue)
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var config = new Config
        {
            Id = Guid.NewGuid(),
            Key = "any",
            Name = "Any",
            Type = type,
            DefaultValue = JsonDocument.Parse(rawJsonValue).RootElement.Clone(),
            EnvironmentId = envId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await host.Store.Configs.UpsertAsync(envId, config, "test", ct);

        var loaded = await host.Store.Configs.GetAsync(envId, "any", ct);

        loaded.Should().NotBeNull();
        loaded!.Type.Should().Be(type);
        loaded.DefaultValue.GetRawText().Should().Be(rawJsonValue);
    }

    [Fact]
    public async Task GetMostRecentUpdate_tracks_updates()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envId = Guid.NewGuid();

        var initial = await host.Store.Configs.GetMostRecentUpdateAsync(envId, ct);
        initial.Should().BeNull();

        await host.Store.Configs.UpsertAsync(envId, NewIntConfig(envId, "first"), "t", ct);
        var after = await host.Store.Configs.GetMostRecentUpdateAsync(envId, ct);
        after.Should().NotBeNull();
    }

    private static Config NewIntConfig(Guid envId, string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Type = ConfigType.Int,
        DefaultValue = JsonSerializer.SerializeToElement(30),
        EnvironmentId = envId,
        Tags = ["checkout", "perf"],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "test",
        UpdatedBy = "test",
    };
}
