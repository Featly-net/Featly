using System.Text.Json;

namespace Featly;

/// <summary>
/// A single targeting predicate. Compares the attribute pulled from
/// <see cref="EvaluationContext"/> against <see cref="Value"/> using
/// <see cref="Operator"/>. <see cref="Negate"/> wraps the whole thing in a NOT.
/// </summary>
public sealed class Condition
{
    /// <summary>
    /// Dot-separated path into the evaluation context, for example
    /// <c>user.country</c> or <c>request.ip</c>. The engine resolves the path
    /// against <see cref="EvaluationContext.Attributes"/>; the special path
    /// <c>targetingKey</c> resolves to <see cref="EvaluationContext.TargetingKey"/>.
    /// </summary>
    public required string Attribute { get; set; }

    /// <summary>Comparison to apply.</summary>
    public required ConditionOperator Operator { get; set; }

    /// <summary>
    /// The literal compared against the attribute. For most operators it is a
    /// scalar; for <see cref="ConditionOperator.In"/> / <see cref="ConditionOperator.NotIn"/>
    /// it is a JSON array; for <see cref="ConditionOperator.InSegment"/> it is
    /// a JSON string with the segment key.
    /// </summary>
    public required JsonElement Value { get; set; }

    /// <summary>When <c>true</c>, the overall predicate is logically NOTted.</summary>
    public bool Negate { get; set; }
}
