using System.Text.Json;
using AwesomeAssertions;
using Featly.Engine;
using Xunit;

namespace Featly.Engine.Tests;

/// <summary>
/// Mirrors <see cref="EvaluatorTests"/> and <see cref="RuleAndSegmentTests"/>
/// but for <see cref="Evaluator.EvaluateConfig"/>. Configs share the targeting
/// machinery with flags but produce a typed value directly — there's no variant
/// indirection.
/// </summary>
public class ConfigEvaluatorTests
{
    private static readonly Guid EnvId = Guid.NewGuid();
    private static readonly JsonElement StringFallback = JsonSerializer.SerializeToElement("fallback");

    [Fact]
    public void Returns_NotFound_when_config_is_null()
    {
        var result = Evaluator.EvaluateConfig(config: null, context: null, StringFallback);

        result.Reason.Should().Be(EvaluationReason.NotFound);
        result.Value.GetString().Should().Be("fallback");
    }

    [Fact]
    public void Returns_NotFound_when_config_is_archived()
    {
        var config = BuildConfig(archived: true, defaultValue: JsonSerializer.SerializeToElement("primary"));

        var result = Evaluator.EvaluateConfig(config, context: null, StringFallback);

        result.Reason.Should().Be(EvaluationReason.NotFound);
        result.Value.GetString().Should().Be("fallback");
    }

    [Fact]
    public void Returns_default_value_when_no_rules_defined()
    {
        var config = BuildConfig(defaultValue: JsonSerializer.SerializeToElement("primary"));

        var result = Evaluator.EvaluateConfig(config, context: null, StringFallback);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.Value.GetString().Should().Be("primary");
        result.Key.Should().Be(config.Key);
    }

    [Fact]
    public void Returns_default_value_when_no_rule_matches()
    {
        var config = BuildConfig(
            defaultValue: JsonSerializer.SerializeToElement(30),
            rules: new ConfigRule
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
                Value = JsonSerializer.SerializeToElement(60),
            });

        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "US" });
        var result = Evaluator.EvaluateConfig(config, ctx, StringFallback);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.Value.GetInt32().Should().Be(30);
    }

    [Fact]
    public void Returns_rule_value_when_rule_matches()
    {
        var config = BuildConfig(
            defaultValue: JsonSerializer.SerializeToElement(30),
            rules: new ConfigRule
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
                Value = JsonSerializer.SerializeToElement(60),
            });

        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" });
        var result = Evaluator.EvaluateConfig(config, ctx, StringFallback);

        result.Reason.Should().Be(EvaluationReason.TargetingMatch);
        result.RuleMatched.Should().Be("BR");
        result.Value.GetInt32().Should().Be(60);
    }

    [Fact]
    public void First_matching_rule_wins_in_order()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?>
        {
            ["user.country"] = "BR",
            ["user.plan"] = "pro",
        });

        var config = BuildConfig(
            defaultValue: JsonSerializer.SerializeToElement(30),
            rules:
            [
                new ConfigRule
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
                    Value = JsonSerializer.SerializeToElement(60),
                },
                new ConfigRule
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
                    Value = JsonSerializer.SerializeToElement(120),
                },
            ]);

        var result = Evaluator.EvaluateConfig(config, ctx, StringFallback);

        result.Reason.Should().Be(EvaluationReason.TargetingMatch);
        result.RuleMatched.Should().Be("BR");
        result.Value.GetInt32().Should().Be(60);
    }

    [Fact]
    public void Disabled_rule_is_skipped()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" });
        var config = BuildConfig(
            defaultValue: JsonSerializer.SerializeToElement(30),
            rules: new ConfigRule
            {
                Order = 0,
                Enabled = false,
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
                Value = JsonSerializer.SerializeToElement(60),
            });

        var result = Evaluator.EvaluateConfig(config, ctx, StringFallback);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.Value.GetInt32().Should().Be(30);
    }

    [Fact]
    public void Conditions_within_a_rule_are_ANDed()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?>
        {
            ["user.country"] = "BR",
            ["user.plan"] = "free",
        });

        var config = BuildConfig(
            defaultValue: JsonSerializer.SerializeToElement(30),
            rules: new ConfigRule
            {
                Order = 0,
                Name = "BR-PRO",
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
                Value = JsonSerializer.SerializeToElement(60),
            });

        var result = Evaluator.EvaluateConfig(config, ctx, StringFallback);

        // user.plan doesn't match pro -> AND fails -> default value.
        result.Reason.Should().Be(EvaluationReason.Default);
        result.Value.GetInt32().Should().Be(30);
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
        var config = BuildConfig(
            defaultValue: JsonSerializer.SerializeToElement(30),
            rules: new ConfigRule
            {
                Order = 0,
                Name = "enterprise-only",
                Conditions =
                [
                    new Condition
                    {
                        Attribute = "ignored",
                        Operator = ConditionOperator.InSegment,
                        Value = JsonSerializer.SerializeToElement("enterprise"),
                    },
                ],
                Value = JsonSerializer.SerializeToElement(300),
            });

        var result = Evaluator.EvaluateConfig(config, ctx, StringFallback, lookup);

        result.Reason.Should().Be(EvaluationReason.TargetingMatch);
        result.RuleMatched.Should().Be("enterprise-only");
        result.Value.GetInt32().Should().Be(300);
    }

    [Fact]
    public void Returns_json_payload_intact_for_object_typed_configs()
    {
        var payload = JsonSerializer.SerializeToElement(new { feature = "checkout", retries = 3 });
        var config = BuildConfig(defaultValue: payload, type: ConfigType.Json);

        var result = Evaluator.EvaluateConfig(config, context: null, StringFallback);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.Value.GetProperty("feature").GetString().Should().Be("checkout");
        result.Value.GetProperty("retries").GetInt32().Should().Be(3);
    }

    private static Config BuildConfig(
        JsonElement defaultValue,
        ConfigType type = ConfigType.Int,
        bool archived = false,
        params ConfigRule[] rules)
    {
        return new Config
        {
            Id = Guid.NewGuid(),
            Key = "checkout.timeout",
            Name = "Checkout Timeout",
            Type = type,
            DefaultValue = defaultValue,
            Rules = [.. rules],
            EnvironmentId = EnvId,
            Archived = archived,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}
