using System.Text.Json;
using AwesomeAssertions;
using Featly.Engine;
using Xunit;

namespace Featly.Engine.Tests;

/// <summary>
/// Bucketing is the part of the engine where determinism and distribution
/// matter the most. These tests pin the contract: same subject + same flag
/// = same variant, and a 50/50 split actually splits roughly 50/50.
/// </summary>
public class BucketingTests
{
    private static readonly Guid EnvId = Guid.NewGuid();
    private static readonly JsonElement Fallback = JsonSerializer.SerializeToElement(false);

    [Fact]
    public void Same_subject_lands_on_the_same_variant_across_evaluations()
    {
        var flag = NewSplitFlag(50, 50);
        var ctx = new EvaluationContext(TargetingKey: "user-12345");

        var first = Evaluator.EvaluateFlag(flag, ctx, Fallback);
        var second = Evaluator.EvaluateFlag(flag, ctx, Fallback);
        var third = Evaluator.EvaluateFlag(flag, ctx, Fallback);

        second.VariantKey.Should().Be(first.VariantKey);
        third.VariantKey.Should().Be(first.VariantKey);
        first.Reason.Should().Be(EvaluationReason.Split);
    }

    [Fact]
    public void Split_50_50_distributes_within_5_percent_over_5000_subjects()
    {
        var flag = NewSplitFlag(50, 50);
        var onCount = 0;
        const int subjects = 5000;

        for (var i = 0; i < subjects; i++)
        {
            var ctx = new EvaluationContext(TargetingKey: $"user-{i}");
            var r = Evaluator.EvaluateFlag(flag, ctx, Fallback);
            if (r.VariantKey == "on")
            {
                onCount++;
            }
        }

        var ratio = onCount / (double)subjects;
        ratio.Should().BeInRange(0.45, 0.55,
            because: "MurmurHash3 over an unbiased subject space should split 50/50 within ±5% over 5 000 trials");
    }

    [Fact]
    public void Split_90_10_distributes_within_3_percent_over_5000_subjects()
    {
        var flag = NewSplitFlag(90, 10);
        var onCount = 0;
        const int subjects = 5000;

        for (var i = 0; i < subjects; i++)
        {
            var ctx = new EvaluationContext(TargetingKey: $"user-{i}");
            var r = Evaluator.EvaluateFlag(flag, ctx, Fallback);
            if (r.VariantKey == "on")
            {
                onCount++;
            }
        }

        var ratio = onCount / (double)subjects;
        ratio.Should().BeInRange(0.87, 0.93,
            because: "a 90/10 split should land roughly 90% in the first bucket over 5 000 trials");
    }

    [Fact]
    public void Split_falls_through_to_default_when_targeting_key_is_missing()
    {
        var flag = NewSplitFlag(50, 50);
        var ctx = new EvaluationContext(); // no TargetingKey

        var result = Evaluator.EvaluateFlag(flag, ctx, Fallback);

        result.VariantKey.Should().Be("off");
        result.Reason.Should().Be(EvaluationReason.Default);
    }

    private static Flag NewSplitFlag(int onWeight, int offWeight)
    {
        return new Flag
        {
            Id = Guid.NewGuid(),
            Key = "split-demo",
            Name = "Split demo",
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
                    Name = "Split",
                    Conditions = [], // empty conditions = matches every subject
                    Outcome = new RuleOutcome
                    {
                        Splits =
                        [
                            new Split { VariantKey = "on",  Weight = onWeight },
                            new Split { VariantKey = "off", Weight = offWeight },
                        ],
                    },
                },
            ],
        };
    }
}
