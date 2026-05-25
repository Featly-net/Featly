namespace Featly;

/// <summary>
/// Detailed evaluation outcome, carrying both the returned value and the
/// metadata callers need for logging, tracing, and analytics.
/// </summary>
/// <typeparam name="T">The value type returned by the evaluation.</typeparam>
public sealed record EvaluationResult<T>(
    string Key,
    T Value,
    string VariantKey,
    EvaluationReason Reason,
    string? RuleMatched = null,
    string? Error = null);
