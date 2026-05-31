using System.Text.Json;
using AwesomeAssertions;
using Featly.Sdk.Internal;
using Xunit;

namespace Featly.Sdk.Tests;

public class FlagClientTests
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
    public async Task IsEnabledAsync_returns_false_when_cache_is_empty()
    {
        var cache = new FeatlySnapshotCache();
        var client = new FlagClient(cache, new NoOpAccessor());

        var enabled = await client.IsEnabledAsync("missing-flag", ct: TestContext.Current.CancellationToken);

        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_returns_default_variant_when_no_rules_match()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(BuildSnapshot(enabled: true, defaultVariantKey: "on"), etag: "\"abc\"");
        var client = new FlagClient(cache, new NoOpAccessor());

        var enabled = await client.IsEnabledAsync("demo", ct: TestContext.Current.CancellationToken);

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_returns_default_variant_when_kill_switch_is_off()
    {
        // ARCHITECTURE.md §5: when Enabled=false the engine returns the default variant
        // with reason=Disabled. Operators wire DefaultVariantKey="off" when they want the
        // kill switch to mean "false".
        var cache = new FeatlySnapshotCache();
        cache.Replace(BuildSnapshot(enabled: false, defaultVariantKey: "off"), etag: "\"abc\"");
        var client = new FlagClient(cache, new NoOpAccessor());

        var enabled = await client.IsEnabledAsync("demo", ct: TestContext.Current.CancellationToken);

        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_carries_reason_and_variant()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(BuildSnapshot(enabled: true, defaultVariantKey: "on"), etag: "\"abc\"");
        var client = new FlagClient(cache, new NoOpAccessor());

        var result = await client.EvaluateAsync("demo", defaultValue: false, ct: TestContext.Current.CancellationToken);

        result.Value.Should().BeTrue();
        result.VariantKey.Should().Be("on");
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    private static ConfigSnapshot BuildSnapshot(bool enabled, string defaultVariantKey)
    {
        var envId = Guid.NewGuid();
        var flag = new Flag
        {
            Id = Guid.NewGuid(),
            Key = "demo",
            Name = "Demo",
            Type = FlagType.Boolean,
            Enabled = enabled,
            DefaultVariantKey = defaultVariantKey,
            EnvironmentId = envId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Variants =
            [
                new Variant { Key = "on", Name = "On", Value = JsonSerializer.SerializeToElement(true) },
                new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
            ],
        };

        return new ConfigSnapshot(
            EnvironmentId: envId,
            EnvironmentKey: "development",
            At: DateTimeOffset.UtcNow,
            Flags: [flag],
            Segments: [],
            Configs: []);
    }
}
