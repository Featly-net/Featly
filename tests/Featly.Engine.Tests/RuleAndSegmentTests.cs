using System.Text.Json;
using Featly.Engine;
using FluentAssertions;
using Xunit;

namespace Featly.Engine.Tests;

public class RuleAndSegmentTests
{
    private static readonly Guid EnvId = Guid.NewGuid();
    private static readonly JsonElement Fallback = JsonSerializer.SerializeToElement(false);

    [Fact]
    public void First_matching_rule_wins_in_order()
    {
        // Two rules: BR -> on, PRO plan -> custom. BR user with PRO plan should match the first.
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?>
        {
            ["user.country"] = "BR",
            ["user.plan"] = "pro",
        });

        var flag = BuildFlag(
            new Rule
            {
                Order = 0,
                Name = "BR",
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
                Name = "PRO",
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "user.plan",
                        Operator = ConditionOperator.Equals,
                        Value = JsonSerializer.SerializeToElement("pro"),
                    },
                ],
                Outcome = new RuleOutcome { VariantKey = "off" },
            });

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback);

        result.Reason.Should().Be(EvaluationReason.TargetingMatch);
        result.VariantKey.Should().Be("on");
        result.RuleMatched.Should().Be("BR");
    }

    [Fact]
    public void Conditions_within_a_rule_are_ANDed()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?>
        {
            ["user.country"] = "BR",
            ["user.plan"] = "free",
        });

        var flag = BuildFlag(new Rule
        {
            Order = 0,
            Conditions =
            [
                new Condition
                {
                    Attribute = "user.country",
                    Operator = ConditionOperator.Equals,
                    Value = JsonSerializer.SerializeToElement("BR"),
                },
                new Condition
                {
                    Attribute = "user.plan",
                    Operator = ConditionOperator.Equals,
                    Value = JsonSerializer.SerializeToElement("pro"),
                },
            ],
            Outcome = new RuleOutcome { VariantKey = "on" },
        });

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback);

        // user.plan doesn't match pro -> AND fails -> default.
        result.VariantKey.Should().Be("off");
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    [Fact]
    public void Disabled_rule_is_skipped()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" });
        var flag = BuildFlag(new Rule
        {
            Order = 0,
            Enabled = false,
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
        });

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback);

        result.VariantKey.Should().Be("off");
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    [Fact]
    public void InSegment_matches_when_subject_satisfies_segment_conditions()
    {
        var enterprise = new Segment
        {
            Id = Guid.NewGuid(),
            Key = "enterprise",
            Name = "Enterprise",
            EnvironmentId = EnvId,
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
        var lookup = new DictionarySegmentLookup(new Dictionary<string, Segment> { ["enterprise"] = enterprise });

        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.plan"] = "enterprise" });
        var flag = BuildFlag(new Rule
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
        });

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback, lookup);

        result.VariantKey.Should().Be("on");
        result.Reason.Should().Be(EvaluationReason.TargetingMatch);
    }

    [Fact]
    public void InSegment_fails_when_segment_is_missing_from_lookup()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.plan"] = "enterprise" });
        var flag = BuildFlag(new Rule
        {
            Order = 0,
            Conditions =
            [
                new Condition
                {
                    Attribute = "ignored",
                    Operator = ConditionOperator.InSegment,
                    Value = JsonSerializer.SerializeToElement("ghost-segment"),
                },
            ],
            Outcome = new RuleOutcome { VariantKey = "on" },
        });

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback);

        result.VariantKey.Should().Be("off");
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    private static Flag BuildFlag(params Rule[] rules)
    {
        return new Flag
        {
            Id = Guid.NewGuid(),
            Key = "demo",
            Name = "Demo",
            Type = FlagType.Boolean,
            Enabled = true,
            DefaultVariantKey = "off",
            EnvironmentId = EnvId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Variants =
            [
                new Variant { Key = "on",  Name = "On",  Value = JsonSerializer.SerializeToElement(true) },
                new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
            ],
            Rules = [.. rules],
        };
    }
}
