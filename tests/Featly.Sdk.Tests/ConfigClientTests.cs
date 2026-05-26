using System.Text.Json;
using Featly.Sdk.Internal;
using FluentAssertions;
using Xunit;

namespace Featly.Sdk.Tests;

/// <summary>
/// Validates <see cref="ConfigClient"/>: SDK-side local evaluation against the
/// cached snapshot, ambient context fallback, and typed deserialization.
/// </summary>
public class ConfigClientTests
{
    private sealed class NoOpAccessor : IFeatlyContextAccessor
    {
        public EvaluationContext? Current => null;
    }

    private sealed class FixedAccessor(EvaluationContext context) : IFeatlyContextAccessor
    {
        public EvaluationContext? Current { get; } = context;
    }

    [Fact]
    public async Task GetAsync_returns_default_when_cache_is_empty()
    {
        var cache = new FeatlySnapshotCache();
        var client = new ConfigClient(cache, new NoOpAccessor());

        var value = await client.GetAsync("missing", defaultValue: 30, ct: TestContext.Current.CancellationToken);

        value.Should().Be(30);
    }

    [Fact]
    public async Task GetAsync_returns_default_value_when_no_rule_matches()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(BuildSnapshot(defaultValue: 30), etag: "\"abc\"");
        var client = new ConfigClient(cache, new NoOpAccessor());

        var value = await client.GetAsync("checkout.timeout", defaultValue: 0, ct: TestContext.Current.CancellationToken);

        value.Should().Be(30);
    }

    [Fact]
    public async Task EvaluateAsync_carries_reason_and_rule_name()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(
            BuildSnapshot(defaultValue: 30, ruleCountry: "BR", ruleValue: 60),
            etag: "\"abc\"");
        var client = new ConfigClient(cache, new NoOpAccessor());

        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" });
        var result = await client.EvaluateAsync(
            "checkout.timeout",
            defaultValue: 0,
            ctx,
            TestContext.Current.CancellationToken);

        result.Value.Should().Be(60);
        result.Reason.Should().Be(EvaluationReason.TargetingMatch);
        result.RuleMatched.Should().Be("country=BR");
    }

    [Fact]
    public async Task EvaluateAsync_returns_NotFound_when_key_missing()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(BuildSnapshot(defaultValue: 30), etag: "\"abc\"");
        var client = new ConfigClient(cache, new NoOpAccessor());

        var result = await client.EvaluateAsync("does.not.exist", defaultValue: 7, ct: TestContext.Current.CancellationToken);

        result.Value.Should().Be(7);
        result.Reason.Should().Be(EvaluationReason.NotFound);
    }

    [Fact]
    public async Task GetAsync_uses_ambient_context_when_caller_passes_none()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(
            BuildSnapshot(defaultValue: 30, ruleCountry: "BR", ruleValue: 60),
            etag: "\"abc\"");

        var accessor = new FixedAccessor(new EvaluationContext(
            Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" }));
        var client = new ConfigClient(cache, accessor);

        var value = await client.GetAsync("checkout.timeout", defaultValue: 0, ct: TestContext.Current.CancellationToken);

        value.Should().Be(60);
    }

    [Fact]
    public async Task Explicit_context_overrides_ambient()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(
            BuildSnapshot(defaultValue: 30, ruleCountry: "BR", ruleValue: 60),
            etag: "\"abc\"");

        var accessor = new FixedAccessor(new EvaluationContext(
            Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" }));
        var client = new ConfigClient(cache, accessor);

        var explicitCtx = new EvaluationContext(
            Attributes: new Dictionary<string, object?> { ["user.country"] = "US" });
        var value = await client.GetAsync("checkout.timeout", defaultValue: 0, explicitCtx, TestContext.Current.CancellationToken);

        value.Should().Be(30);
    }

    [Fact]
    public async Task GetAsync_deserializes_string_values()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(BuildStringSnapshot("primary"), etag: "\"abc\"");
        var client = new ConfigClient(cache, new NoOpAccessor());

        var value = await client.GetAsync("theme", defaultValue: "fallback", ct: TestContext.Current.CancellationToken);

        value.Should().Be("primary");
    }

    [Fact]
    public async Task GetAsync_throws_on_blank_key()
    {
        var cache = new FeatlySnapshotCache();
        var client = new ConfigClient(cache, new NoOpAccessor());

        await FluentActions.Invoking(() => client.GetAsync(" ", defaultValue: 0, ct: TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowAsync<ArgumentException>();
    }

    private static ConfigSnapshot BuildSnapshot(int defaultValue, string? ruleCountry = null, int ruleValue = 0)
    {
        var envId = Guid.NewGuid();
        var config = new Config
        {
            Id = Guid.NewGuid(),
            Key = "checkout.timeout",
            Name = "Checkout Timeout",
            Type = ConfigType.Int,
            DefaultValue = JsonSerializer.SerializeToElement(defaultValue),
            EnvironmentId = envId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        if (ruleCountry is not null)
        {
            config.Rules =
            [
                new ConfigRule
                {
                    Order = 0,
                    Name = $"country={ruleCountry}",
                    Conditions =
                    [
                        new Condition
                        {
                            Attribute = "user.country",
                            Operator = ConditionOperator.Equals,
                            Value = JsonSerializer.SerializeToElement(ruleCountry),
                        },
                    ],
                    Value = JsonSerializer.SerializeToElement(ruleValue),
                },
            ];
        }

        return new ConfigSnapshot(
            EnvironmentId: envId,
            EnvironmentKey: "development",
            At: DateTimeOffset.UtcNow,
            Flags: [],
            Segments: [],
            Configs: [config]);
    }

    private static ConfigSnapshot BuildStringSnapshot(string defaultValue)
    {
        var envId = Guid.NewGuid();
        var config = new Config
        {
            Id = Guid.NewGuid(),
            Key = "theme",
            Name = "Theme",
            Type = ConfigType.String,
            DefaultValue = JsonSerializer.SerializeToElement(defaultValue),
            EnvironmentId = envId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        return new ConfigSnapshot(
            EnvironmentId: envId,
            EnvironmentKey: "development",
            At: DateTimeOffset.UtcNow,
            Flags: [],
            Segments: [],
            Configs: [config]);
    }
}
