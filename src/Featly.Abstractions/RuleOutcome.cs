namespace Featly;

/// <summary>
/// What a <see cref="Rule"/> serves when its conditions match. Exactly one of
/// <see cref="VariantKey"/> or <see cref="Splits"/> is set: the first selects
/// a single variant deterministically, the second selects by weighted
/// bucketing of the subject.
/// </summary>
public sealed class RuleOutcome
{
    /// <summary>The variant served to every matching subject.</summary>
    public string? VariantKey { get; set; }

    /// <summary>Weighted slices that bucket matching subjects across variants.</summary>
    public List<Split>? Splits { get; set; }
}
