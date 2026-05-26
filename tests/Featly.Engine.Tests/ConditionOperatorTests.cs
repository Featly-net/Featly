using System.Text.Json;
using Featly.Engine;
using FluentAssertions;
using Xunit;

namespace Featly.Engine.Tests;

/// <summary>
/// Operator-by-operator coverage. Each test runs the evaluator end-to-end with
/// a single rule whose outcome is a fixed variant, so we get one assertion per
/// row: the rule either matched (variant = "on") or it didn't (variant = "off").
/// </summary>
public class ConditionOperatorTests
{
    private static readonly Guid EnvId = Guid.NewGuid();
    private static readonly JsonElement Fallback = JsonSerializer.SerializeToElement(false);
    private static readonly string[] CountriesUsCa = ["US", "CA"];

    [Theory]
    [InlineData(ConditionOperator.Equals, "BR", "BR", true)]
    [InlineData(ConditionOperator.Equals, "BR", "US", false)]
    [InlineData(ConditionOperator.NotEquals, "BR", "BR", false)]
    [InlineData(ConditionOperator.NotEquals, "BR", "US", true)]
    [InlineData(ConditionOperator.Contains, "checkout-v2", "v2", true)]
    [InlineData(ConditionOperator.Contains, "checkout-v2", "v9", false)]
    [InlineData(ConditionOperator.StartsWith, "user-12345", "user-", true)]
    [InlineData(ConditionOperator.StartsWith, "user-12345", "admin-", false)]
    [InlineData(ConditionOperator.EndsWith, "12345-prod", "-prod", true)]
    [InlineData(ConditionOperator.EndsWith, "12345-prod", "-dev", false)]
    public void String_operators(ConditionOperator op, string actual, string compare, bool shouldMatch)
    {
        var result = Eval(op, actual, JsonSerializer.SerializeToElement(compare));
        result.VariantKey.Should().Be(shouldMatch ? "on" : "off");
    }

    [Theory]
    [InlineData(ConditionOperator.GreaterThan, 7, 5, true)]
    [InlineData(ConditionOperator.GreaterThan, 5, 5, false)]
    [InlineData(ConditionOperator.GreaterThanOrEqual, 5, 5, true)]
    [InlineData(ConditionOperator.LessThan, 3, 5, true)]
    [InlineData(ConditionOperator.LessThan, 5, 5, false)]
    [InlineData(ConditionOperator.LessThanOrEqual, 5, 5, true)]
    public void Numeric_operators(ConditionOperator op, int actual, int compare, bool shouldMatch)
    {
        var result = Eval(op, actual, JsonSerializer.SerializeToElement(compare));
        result.VariantKey.Should().Be(shouldMatch ? "on" : "off");
    }

    [Fact]
    public void In_matches_when_value_is_in_array()
    {
        var result = Eval(ConditionOperator.In, "BR", JsonSerializer.SerializeToElement(CountriesUsCa));
        result.VariantKey.Should().Be("off");
    }

    [Fact]
    public void In_matches_when_value_is_present()
    {
        var result = Eval(ConditionOperator.In, "US", JsonSerializer.SerializeToElement(CountriesUsCa));
        result.VariantKey.Should().Be("on");
    }

    [Fact]
    public void NotIn_inverts_In_semantics()
    {
        var resultPresent = Eval(ConditionOperator.NotIn, "US", JsonSerializer.SerializeToElement(CountriesUsCa));
        var resultMissing = Eval(ConditionOperator.NotIn, "BR", JsonSerializer.SerializeToElement(CountriesUsCa));
        resultPresent.VariantKey.Should().Be("off");
        resultMissing.VariantKey.Should().Be("on");
    }

    [Theory]
    [InlineData("user-12345", "^user-\\d+$", true)]
    [InlineData("admin-1", "^user-\\d+$", false)]
    public void Matches_uses_regex(string actual, string pattern, bool shouldMatch)
    {
        var result = Eval(ConditionOperator.Matches, actual, JsonSerializer.SerializeToElement(pattern));
        result.VariantKey.Should().Be(shouldMatch ? "on" : "off");
    }

    [Fact]
    public void Matches_returns_no_match_on_invalid_regex_instead_of_throwing()
    {
        var result = Eval(ConditionOperator.Matches, "anything", JsonSerializer.SerializeToElement("(unclosed"));
        result.VariantKey.Should().Be("off");
    }

    [Theory]
    [InlineData(ConditionOperator.SemverGt, "1.2.3", "1.2.2", true)]
    [InlineData(ConditionOperator.SemverGt, "1.2.3", "1.2.3", false)]
    [InlineData(ConditionOperator.SemverGt, "2.0.0", "1.99.99", true)]
    [InlineData(ConditionOperator.SemverLt, "1.2.2", "1.2.3", true)]
    [InlineData(ConditionOperator.SemverEq, "1.2.3", "1.2.3", true)]
    [InlineData(ConditionOperator.SemverEq, "1.2.3", "1.2.4", false)]
    // Prerelease handling: 1.0.0-alpha < 1.0.0
    [InlineData(ConditionOperator.SemverLt, "1.0.0-alpha", "1.0.0", true)]
    public void Semver_operators(ConditionOperator op, string actual, string compare, bool shouldMatch)
    {
        var result = Eval(op, actual, JsonSerializer.SerializeToElement(compare));
        result.VariantKey.Should().Be(shouldMatch ? "on" : "off");
    }

    [Fact]
    public void Negate_inverts_the_match()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" });
        var flag = BuildFlagWithSingleConditionRule(new Condition
        {
            Attribute = "user.country",
            Operator = ConditionOperator.Equals,
            Value = JsonSerializer.SerializeToElement("BR"),
            Negate = true,
        });

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback);
        result.VariantKey.Should().Be("off"); // negated, so a "match" becomes "miss"
    }

    [Fact]
    public void Missing_attribute_falls_through_to_default()
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.plan"] = "free" });
        var flag = BuildFlagWithSingleConditionRule(new Condition
        {
            Attribute = "user.country",
            Operator = ConditionOperator.Equals,
            Value = JsonSerializer.SerializeToElement("BR"),
        });

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback);
        result.VariantKey.Should().Be("off"); // no match -> default variant
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    private static EvaluationResult<JsonElement> Eval(ConditionOperator op, object actual, JsonElement compareValue)
    {
        var ctx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["x"] = actual });
        var flag = BuildFlagWithSingleConditionRule(new Condition
        {
            Attribute = "x",
            Operator = op,
            Value = compareValue,
        });
        return Evaluator.EvaluateFlag(flag, ctx, Fallback);
    }

    private static Flag BuildFlagWithSingleConditionRule(Condition condition)
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
            Rules =
            [
                new Rule
                {
                    Order = 0,
                    Conditions = [condition],
                    Outcome = new RuleOutcome { VariantKey = "on" },
                },
            ],
        };
    }
}
