namespace Featly.Server.Experiments;

/// <summary>
/// Two-proportion z-test for experiment conversion rates (the 2x2 chi-square
/// equivalent). Pure math, in-house — no statistics package, matching the
/// project's SemverComparer/MurmurHash3 precedent of small self-contained
/// implementations over dependencies.
/// </summary>
/// <remarks>
/// The test asks: "could the observed difference between the baseline's and the
/// variant's conversion rates plausibly be chance?" A two-sided p-value below
/// the conventional 0.05 is reported as significant. This is a fixed-horizon
/// test — peeking at a running experiment repeatedly inflates the false-positive
/// rate; sequential analysis remains on the post-1.0 roadmap.
/// </remarks>
public static class SignificanceCalculator
{
    /// <summary>Conventional significance threshold for <see cref="IsSignificant"/>.</summary>
    public const double Alpha = 0.05;

    /// <summary>
    /// Two-sided p-value for the difference between two conversion proportions
    /// (baseline: <paramref name="baselineConversions"/> of
    /// <paramref name="baselineSubjects"/>; variant:
    /// <paramref name="variantConversions"/> of <paramref name="variantSubjects"/>).
    /// Returns <c>null</c> when the test is undefined: an empty arm, or a pooled
    /// rate of exactly 0 or 1 (no variance to test against).
    /// </summary>
    public static double? TwoProportionPValue(
        int baselineConversions, int baselineSubjects,
        int variantConversions, int variantSubjects)
    {
        if (baselineSubjects <= 0 || variantSubjects <= 0)
        {
            return null;
        }

        var pooled = (double)(baselineConversions + variantConversions) / (baselineSubjects + variantSubjects);
        if (pooled <= 0d || pooled >= 1d)
        {
            return null;
        }

        var standardError = Math.Sqrt(pooled * (1d - pooled) * (1d / baselineSubjects + 1d / variantSubjects));
        var difference = (double)variantConversions / variantSubjects - (double)baselineConversions / baselineSubjects;
        var z = difference / standardError;

        return 2d * (1d - NormalCdf(Math.Abs(z)));
    }

    /// <summary>Whether a p-value crosses the <see cref="Alpha"/> threshold.</summary>
    public static bool IsSignificant(double? pValue) => pValue is { } p && p < Alpha;

    /// <summary>
    /// Standard normal CDF via the Abramowitz &amp; Stegun 7.1.26 erf
    /// approximation (max absolute error ~1.5e-7 — far below anything that
    /// matters at the 0.05 threshold).
    /// </summary>
    internal static double NormalCdf(double z) => 0.5 * (1d + Erf(z / Math.Sqrt(2d)));

    private static double Erf(double x)
    {
        var sign = Math.Sign(x);
        x = Math.Abs(x);

        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var t = 1d / (1d + p * x);
        var y = 1d - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
