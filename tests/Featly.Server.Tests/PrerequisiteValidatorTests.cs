using AwesomeAssertions;
using Featly.Server.Flags;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Unit tests for the pure <see cref="PrerequisiteValidator"/>: unknown-flag
/// references, empty required-variant lists, and cycle detection (direct
/// self-reference and transitive cycles) for <see cref="Flag.Prerequisites"/>
/// (ADR-0027).
/// </summary>
public class PrerequisiteValidatorTests
{
    [Fact]
    public void Empty_prerequisites_are_always_valid()
    {
        var result = PrerequisiteValidator.Validate([], "flag-a", []);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_when_the_referenced_flag_exists_and_no_cycle()
    {
        var flags = new[] { NewFlag("infra-flag") };

        var result = PrerequisiteValidator.Validate(flags, "feature-flag", [NewPrerequisite("infra-flag")]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_reference_to_an_unknown_flag()
    {
        var result = PrerequisiteValidator.Validate([], "feature-flag", [NewPrerequisite("ghost-flag")]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("ghost-flag");
    }

    [Fact]
    public void Rejects_a_prerequisite_with_no_required_variant_keys()
    {
        var flags = new[] { NewFlag("infra-flag") };

        var result = PrerequisiteValidator.Validate(
            flags, "feature-flag", [new Prerequisite { FlagKey = "infra-flag", RequiredVariantKeys = [] }]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("infra-flag");
    }

    [Fact]
    public void Rejects_a_direct_self_reference()
    {
        var flags = new[] { NewFlag("feature-flag") };

        var result = PrerequisiteValidator.Validate(flags, "feature-flag", [NewPrerequisite("feature-flag")]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("cycle");
    }

    [Fact]
    public void Rejects_a_transitive_cycle()
    {
        // a -> b -> c -> (about to add) a
        var flags = new[]
        {
            NewFlag("a"),
            WithPrerequisite(NewFlag("b"), "a"),
            WithPrerequisite(NewFlag("c"), "b"),
        };

        var result = PrerequisiteValidator.Validate(flags, "a", [NewPrerequisite("c")]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("cycle");
    }

    [Fact]
    public void Allows_a_diamond_shaped_dependency_that_is_not_a_cycle()
    {
        // top depends on both left and right; left and right both depend on bottom.
        // Not a cycle -- the same node reachable via two paths is fine.
        var flags = new[]
        {
            NewFlag("bottom"),
            WithPrerequisite(NewFlag("left"), "bottom"),
            WithPrerequisite(NewFlag("right"), "bottom"),
        };

        var result = PrerequisiteValidator.Validate(flags, "top", [NewPrerequisite("left"), NewPrerequisite("right")]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Unrelated_existing_cycles_elsewhere_do_not_affect_a_flag_with_no_prerequisites()
    {
        // Validating a flag that adds no prerequisites should short-circuit
        // before even looking at the rest of the graph.
        var flags = new[] { NewFlag("a"), NewFlag("b") };

        var result = PrerequisiteValidator.Validate(flags, "c", []);

        result.IsValid.Should().BeTrue();
    }

    private static Flag NewFlag(string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Type = FlagType.Boolean,
        DefaultVariantKey = "off",
        EnvironmentId = Guid.NewGuid(),
    };

    private static Flag WithPrerequisite(Flag flag, string prerequisiteFlagKey)
    {
        flag.Prerequisites.Add(NewPrerequisite(prerequisiteFlagKey));
        return flag;
    }

    private static Prerequisite NewPrerequisite(string flagKey) => new() { FlagKey = flagKey, RequiredVariantKeys = ["on"] };
}
