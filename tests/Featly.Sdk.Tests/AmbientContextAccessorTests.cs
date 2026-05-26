using System.Text.Json;
using Featly.Sdk.Internal;
using FluentAssertions;
using Xunit;

namespace Featly.Sdk.Tests;

/// <summary>
/// FlagClient should pick up the ambient EvaluationContext when no explicit
/// one is passed. An explicit context always wins.
/// </summary>
public class AmbientContextAccessorTests
{
    [Fact]
    public async Task Picks_up_ambient_context_when_caller_passes_none()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(SnapshotWithTargetingRule(country: "BR"), etag: "etag-1");

        var accessor = new FixedAccessor(new EvaluationContext(
            Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" }));
        var client = new FlagClient(cache, accessor);

        var result = await client.EvaluateAsync("demo", defaultValue: false, ct: TestContext.Current.CancellationToken);

        result.Value.Should().BeTrue();
        result.Reason.Should().Be(EvaluationReason.TargetingMatch);
    }

    [Fact]
    public async Task Explicit_context_overrides_ambient()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(SnapshotWithTargetingRule(country: "BR"), etag: "etag-1");

        var accessor = new FixedAccessor(new EvaluationContext(
            Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" }));
        var client = new FlagClient(cache, accessor);

        // Explicit context says US — should miss the BR rule.
        var explicitCtx = new EvaluationContext(
            Attributes: new Dictionary<string, object?> { ["user.country"] = "US" });

        var result = await client.EvaluateAsync("demo", defaultValue: false, explicitCtx, TestContext.Current.CancellationToken);

        result.Value.Should().BeFalse();
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    [Fact]
    public async Task NoOp_accessor_means_no_ambient_context()
    {
        var cache = new FeatlySnapshotCache();
        cache.Replace(SnapshotWithTargetingRule(country: "BR"), etag: "etag-1");

        var client = new FlagClient(cache, new NoOpFeatlyContextAccessor());
        var result = await client.EvaluateAsync("demo", defaultValue: false, ct: TestContext.Current.CancellationToken);

        // No context -> no attribute lookup -> rule misses -> default.
        result.Value.Should().BeFalse();
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    [Fact]
    public async Task Segments_from_snapshot_are_resolved_locally()
    {
        var envId = Guid.NewGuid();
        var enterprise = new Segment
        {
            Id = Guid.NewGuid(),
            Key = "enterprise",
            Name = "Enterprise",
            EnvironmentId = envId,
            Conditions =
            [
                new Condition
                {
                    Attribute = "user.plan",
                    Operator = ConditionOperator.Equals,
                    Value = JsonSerializer.SerializeToElement("enterprise"),
                },
            ],
        };

        var flag = new Flag
        {
            Id = Guid.NewGuid(),
            Key = "feat",
            Name = "Feat",
            Type = FlagType.Boolean,
            Enabled = true,
            DefaultVariantKey = "off",
            EnvironmentId = envId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Variants =
            [
                new Variant { Key = "on",  Name = "On",  Value = JsonSerializer.SerializeToElement(true) },
                new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
            ],
            Rules =
            [
                new Rule
                {
                    Order = 0,
                    Conditions =
                    [
                        new Condition
                        {
                            Attribute = "ignored",
                            Operator = ConditionOperator.InSegment,
                            Value = JsonSerializer.SerializeToElement("enterprise"),
                        },
                    ],
                    Outcome = new RuleOutcome { VariantKey = "on" },
                },
            ],
        };

        var snapshot = new ConfigSnapshot(envId, "development", DateTimeOffset.UtcNow, [flag], [enterprise]);
        var cache = new FeatlySnapshotCache();
        cache.Replace(snapshot, etag: "etag-1");

        var client = new FlagClient(cache, new NoOpFeatlyContextAccessor());

        var matches = await client.EvaluateAsync(
            "feat",
            defaultValue: false,
            new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.plan"] = "enterprise" }),
            TestContext.Current.CancellationToken);

        matches.Value.Should().BeTrue();
        matches.Reason.Should().Be(EvaluationReason.TargetingMatch);
    }

    private static ConfigSnapshot SnapshotWithTargetingRule(string country)
    {
        var envId = Guid.NewGuid();
        var flag = new Flag
        {
            Id = Guid.NewGuid(),
            Key = "demo",
            Name = "Demo",
            Type = FlagType.Boolean,
            Enabled = true,
            DefaultVariantKey = "off",
            EnvironmentId = envId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Variants =
            [
                new Variant { Key = "on",  Name = "On",  Value = JsonSerializer.SerializeToElement(true) },
                new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
            ],
            Rules =
            [
                new Rule
                {
                    Order = 0,
                    Conditions =
                    [
                        new Condition
                        {
                            Attribute = "user.country",
                            Operator = ConditionOperator.Equals,
                            Value = JsonSerializer.SerializeToElement(country),
                        },
                    ],
                    Outcome = new RuleOutcome { VariantKey = "on" },
                },
            ],
        };

        return new ConfigSnapshot(envId, "development", DateTimeOffset.UtcNow, [flag], []);
    }

    private sealed class FixedAccessor(EvaluationContext context) : IFeatlyContextAccessor
    {
        public EvaluationContext? Current { get; } = context;
    }
}
