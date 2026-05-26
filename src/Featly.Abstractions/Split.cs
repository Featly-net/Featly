namespace Featly;

/// <summary>
/// One slice of a percentage rollout. The engine picks a variant by hashing
/// the subject's <see cref="EvaluationContext.TargetingKey"/> into a 0-9999
/// bucket and walking the cumulative <see cref="Weight"/> of each split.
/// </summary>
/// <remarks>
/// All splits in a single <see cref="RuleOutcome"/> must sum to <c>100</c>.
/// The engine validates this at the API layer; the storage layer trusts the
/// caller.
/// </remarks>
public sealed class Split
{
    /// <summary>Variant served when this split wins the bucket.</summary>
    public required string VariantKey { get; init; }

    /// <summary>Percentage weight from 0 to 100 inclusive.</summary>
    public required int Weight { get; init; }
}
