using System.Text.Json;
using AwesomeAssertions;
using Featly.Engine;
using Xunit;

namespace Featly.Engine.Tests;

public class PrerequisiteTests
{
    private static readonly Guid EnvId = Guid.NewGuid();
    private static readonly JsonElement Fallback = JsonSerializer.SerializeToElement(false);

    [Fact]
    public void Ignores_prerequisites_and_stays_on_the_zero_cost_path_when_the_lookup_is_null()
    {
        var flag = BuildFlag("dependent", enabled: true, defaultVariantKey: "on", prerequisites: []);

        var result = Evaluator.EvaluateFlag(flag, context: null, Fallback);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.VariantKey.Should().Be("on");
    }

    [Fact]
    public void Resolves_default_variant_with_PrerequisiteNotMet_when_the_prerequisite_flag_is_disabled()
    {
        var infra = BuildFlag("infra", enabled: false, defaultVariantKey: "off");
        var dependent = BuildFlag(
            "dependent",
            enabled: true,
            prerequisites: [new Prerequisite { FlagKey = "infra", RequiredVariantKeys = ["on"] }]);
        var lookup = new DictionaryFlagLookup(new Dictionary<string, Flag> { ["infra"] = infra });

        var result = Evaluator.EvaluateFlag(dependent, context: null, Fallback, flags: lookup);

        result.Reason.Should().Be(EvaluationReason.PrerequisiteNotMet);
        result.VariantKey.Should().Be("off");
    }

    [Fact]
    public void Evaluates_own_rules_when_the_prerequisite_flag_resolves_to_a_required_variant()
    {
        var infra = BuildFlag("infra", enabled: true, defaultVariantKey: "on");
        var dependent = BuildFlag(
            "dependent",
            enabled: true,
            defaultVariantKey: "on",
            prerequisites: [new Prerequisite { FlagKey = "infra", RequiredVariantKeys = ["on"] }]);
        var lookup = new DictionaryFlagLookup(new Dictionary<string, Flag> { ["infra"] = infra });

        var result = Evaluator.EvaluateFlag(dependent, context: null, Fallback, flags: lookup);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.VariantKey.Should().Be("on");
    }

    [Fact]
    public void Treats_a_missing_prerequisite_flag_as_unmet()
    {
        var dependent = BuildFlag(
            "dependent",
            enabled: true,
            defaultVariantKey: "off",
            prerequisites: [new Prerequisite { FlagKey = "ghost", RequiredVariantKeys = ["on"] }]);

        var result = Evaluator.EvaluateFlag(dependent, context: null, Fallback, flags: DictionaryFlagLookup.Empty);

        result.Reason.Should().Be(EvaluationReason.PrerequisiteNotMet);
        result.VariantKey.Should().Be("off");
    }

    [Fact]
    public void Any_required_variant_key_satisfies_a_single_prerequisite_OR_within()
    {
        var infra = BuildFlag("infra", enabled: true, defaultVariantKey: "beta");
        var dependent = BuildFlag(
            "dependent",
            enabled: true,
            defaultVariantKey: "on",
            prerequisites: [new Prerequisite { FlagKey = "infra", RequiredVariantKeys = ["on", "beta"] }]);
        var lookup = new DictionaryFlagLookup(new Dictionary<string, Flag> { ["infra"] = infra });

        var result = Evaluator.EvaluateFlag(dependent, context: null, Fallback, flags: lookup);

        result.Reason.Should().Be(EvaluationReason.Default);
    }

    [Fact]
    public void All_prerequisites_must_be_met_AND_across()
    {
        var infraA = BuildFlag("infra-a", enabled: true, defaultVariantKey: "on");
        var infraB = BuildFlag("infra-b", enabled: false, defaultVariantKey: "off");
        var dependent = BuildFlag(
            "dependent",
            enabled: true,
            defaultVariantKey: "off",
            prerequisites:
            [
                new Prerequisite { FlagKey = "infra-a", RequiredVariantKeys = ["on"] },
                new Prerequisite { FlagKey = "infra-b", RequiredVariantKeys = ["on"] },
            ]);
        var lookup = new DictionaryFlagLookup(new Dictionary<string, Flag>
        {
            ["infra-a"] = infraA,
            ["infra-b"] = infraB,
        });

        var result = Evaluator.EvaluateFlag(dependent, context: null, Fallback, flags: lookup);

        result.Reason.Should().Be(EvaluationReason.PrerequisiteNotMet);
    }

    [Fact]
    public void Chained_prerequisites_resolve_transitively()
    {
        var root = BuildFlag("root", enabled: true, defaultVariantKey: "on");
        var middle = BuildFlag(
            "middle",
            enabled: true,
            defaultVariantKey: "on",
            prerequisites: [new Prerequisite { FlagKey = "root", RequiredVariantKeys = ["on"] }]);
        var leaf = BuildFlag(
            "leaf",
            enabled: true,
            defaultVariantKey: "on",
            prerequisites: [new Prerequisite { FlagKey = "middle", RequiredVariantKeys = ["on"] }]);
        var lookup = new DictionaryFlagLookup(new Dictionary<string, Flag>
        {
            ["root"] = root,
            ["middle"] = middle,
        });

        var result = Evaluator.EvaluateFlag(leaf, context: null, Fallback, flags: lookup);

        result.Reason.Should().Be(EvaluationReason.Default);
        result.VariantKey.Should().Be("on");
    }

    private static Flag BuildFlag(
        string key,
        bool enabled,
        string defaultVariantKey = "off",
        List<Prerequisite>? prerequisites = null)
    {
        return new Flag
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = key,
            Type = FlagType.Boolean,
            Enabled = enabled,
            DefaultVariantKey = defaultVariantKey,
            EnvironmentId = EnvId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Prerequisites = prerequisites ?? [],
            Variants =
            [
                new Variant { Key = "on", Name = "On", Value = JsonSerializer.SerializeToElement(true) },
                new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement(false) },
                new Variant { Key = "beta", Name = "Beta", Value = JsonSerializer.SerializeToElement("beta") },
            ],
        };
    }
}
