using System.Text.Json;
using Featly.Engine;
using FluentAssertions;
using Xunit;

namespace Featly.Engine.Tests;

public class EvaluatorTests
{
    private static readonly Guid EnvId = Guid.NewGuid();
    private static readonly JsonElement BooleanFallback = JsonSerializer.SerializeToElement(false);

    [Fact]
    public void Returns_NotFound_when_flag_is_null()
    {
        var result = Evaluator.EvaluateFlag(flag: null, context: null, BooleanFallback);

        result.Reason.Should().Be(EvaluationReason.NotFound);
        result.As(false).Should().BeFalse();
    }

    [Fact]
    public void Returns_NotFound_when_flag_is_archived()
    {
        var flag = NewFlag(enabled: true, archived: true);

        var result = Evaluator.EvaluateFlag(flag, context: null, BooleanFallback);

        result.Reason.Should().Be(EvaluationReason.NotFound);
        result.As(false).Should().BeFalse();
    }

    [Fact]
    public void Returns_Disabled_with_default_variant_when_kill_switch_is_off()
    {
        var flag = NewFlag(enabled: false, defaultVariantKey: "off");

        var result = Evaluator.EvaluateFlag(flag, context: null, BooleanFallback);

        result.Reason.Should().Be(EvaluationReason.Disabled);
        result.VariantKey.Should().Be("off");
        result.As(true).Should().BeFalse();
    }

    [Fact]
    public void Returns_Default_when_enabled_and_no_rules()
    {
        var flag = NewFlag(enabled: true, defaultVariantKey: "on");

        var result = Evaluator.EvaluateFlag(flag, context: null, BooleanFallback);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.VariantKey.Should().Be("on");
        result.As(false).Should().BeTrue();
    }

    [Fact]
    public void Returns_Error_when_default_variant_is_missing()
    {
        var flag = NewFlag(enabled: true, defaultVariantKey: "ghost");

        var result = Evaluator.EvaluateFlag(flag, context: null, BooleanFallback);

        result.Reason.Should().Be(EvaluationReason.Error);
        result.Error.Should().Contain("ghost");
        result.As(true).Should().BeTrue();
    }

    private static Flag NewFlag(bool enabled = true, bool archived = false, string defaultVariantKey = "off")
    {
        return new Flag
        {
            Id = Guid.NewGuid(),
            Key = "demo",
            Name = "Demo",
            Type = FlagType.Boolean,
            Enabled = enabled,
            Archived = archived,
            DefaultVariantKey = defaultVariantKey,
            EnvironmentId = EnvId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Variants =
            [
                new Variant { Key = "on", Name = "On", Value = JsonSerializer.SerializeToElement(true) },
                new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
            ],
        };
    }
}
