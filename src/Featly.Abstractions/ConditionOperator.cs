namespace Featly;

/// <summary>
/// The 16 operators the targeting engine supports. Each operator describes how
/// to compare the attribute pulled from <see cref="EvaluationContext"/> against
/// the literal in <see cref="Condition.Value"/>.
/// </summary>
/// <remarks>
/// Ordering is stable: do not reorder these members. They are persisted by name
/// (string) in the storage layer, so removing or renaming a member is a
/// breaking change to the on-disk format.
/// </remarks>
public enum ConditionOperator
{
    /// <summary>Strict equality.</summary>
    Equals,

    /// <summary>Inverse of <see cref="Equals"/>.</summary>
    NotEquals,

    /// <summary>The attribute value is one of the items in <see cref="Condition.Value"/> (which must be a JSON array).</summary>
    In,

    /// <summary>Inverse of <see cref="In"/>.</summary>
    NotIn,

    /// <summary>Numeric greater-than.</summary>
    GreaterThan,

    /// <summary>Numeric greater-than-or-equal.</summary>
    GreaterThanOrEqual,

    /// <summary>Numeric less-than.</summary>
    LessThan,

    /// <summary>Numeric less-than-or-equal.</summary>
    LessThanOrEqual,

    /// <summary>String contains substring (case-sensitive).</summary>
    Contains,

    /// <summary>String starts with prefix (case-sensitive).</summary>
    StartsWith,

    /// <summary>String ends with suffix (case-sensitive).</summary>
    EndsWith,

    /// <summary>Regex match. <see cref="Condition.Value"/> is a regex literal.</summary>
    Matches,

    /// <summary>Semantic version comparison: attribute &gt; value.</summary>
    SemverGt,

    /// <summary>Semantic version comparison: attribute &lt; value.</summary>
    SemverLt,

    /// <summary>Semantic version comparison: attribute equals value (after parsing).</summary>
    SemverEq,

    /// <summary>The subject is in the named segment. <see cref="Condition.Value"/> is the segment key.</summary>
    InSegment,
}
