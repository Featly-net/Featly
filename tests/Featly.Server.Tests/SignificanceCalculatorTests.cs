using AwesomeAssertions;
using Featly.Server.Experiments;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Two-proportion z-test used to flag a statistically significant conversion
/// difference between an experiment variant and its baseline.
/// </summary>
public class SignificanceCalculatorTests
{
    [Fact]
    public void Large_clear_difference_is_significant()
    {
        // control: 10/100 (10%), treatment: 25/100 (25%) — the same shape used
        // in the aggregator's end-to-end test.
        var p = SignificanceCalculator.TwoProportionPValue(10, 100, 25, 100);

        p.Should().NotBeNull();
        p!.Value.Should().BeLessThan(SignificanceCalculator.Alpha);
        SignificanceCalculator.IsSignificant(p).Should().BeTrue();
    }

    [Fact]
    public void Small_difference_at_low_volume_is_not_significant()
    {
        // control: 10/100 (10%), treatment: 11/100 (11%) — noise-level gap.
        var p = SignificanceCalculator.TwoProportionPValue(10, 100, 11, 100);

        p.Should().NotBeNull();
        p!.Value.Should().BeGreaterThan(SignificanceCalculator.Alpha);
        SignificanceCalculator.IsSignificant(p).Should().BeFalse();
    }

    [Fact]
    public void Identical_rates_yield_a_p_value_of_one()
    {
        var p = SignificanceCalculator.TwoProportionPValue(50, 500, 50, 500);

        p.Should().NotBeNull();
        p!.Value.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Is_symmetric_regardless_of_which_arm_is_baseline()
    {
        var ab = SignificanceCalculator.TwoProportionPValue(10, 100, 25, 100);
        var ba = SignificanceCalculator.TwoProportionPValue(25, 100, 10, 100);

        ab.Should().BeApproximately(ba!.Value, 1e-12);
    }

    [Theory]
    [InlineData(0, 0, 5, 100)]   // empty baseline
    [InlineData(5, 100, 0, 0)]   // empty variant
    public void Empty_arm_returns_null(int baseConv, int baseSubj, int varConv, int varSubj)
    {
        SignificanceCalculator.TwoProportionPValue(baseConv, baseSubj, varConv, varSubj).Should().BeNull();
    }

    [Fact]
    public void Zero_conversions_everywhere_returns_null()
    {
        // Pooled rate is exactly 0 — no variance to test against.
        SignificanceCalculator.TwoProportionPValue(0, 50, 0, 50).Should().BeNull();
    }

    [Fact]
    public void Everyone_converts_returns_null()
    {
        // Pooled rate is exactly 1 — no variance to test against.
        SignificanceCalculator.TwoProportionPValue(50, 50, 50, 50).Should().BeNull();
    }

    [Fact]
    public void Null_p_value_is_never_significant()
    {
        SignificanceCalculator.IsSignificant(null).Should().BeFalse();
    }
}
