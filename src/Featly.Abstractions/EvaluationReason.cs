namespace Featly;

/// <summary>
/// Why the engine returned the value it did. Surfaced on
/// <see cref="EvaluationResult{T}"/> so applications and observability
/// pipelines can distinguish a default fallback from a matched rule.
/// </summary>
public enum EvaluationReason
{
    /// <summary>The flag or config carried a static value with no rule indirection.</summary>
    Static,

    /// <summary>A targeting rule matched and selected the result.</summary>
    TargetingMatch,

    /// <summary>A targeting rule matched and the result was chosen by weighted bucketing.</summary>
    Split,

    /// <summary>No rule matched. The default variant or default value was used.</summary>
    Default,

    /// <summary>The flag is present but disabled via its kill switch.</summary>
    Disabled,

    /// <summary>An error occurred during evaluation. The caller's default was returned.</summary>
    Error,

    /// <summary>The requested key was not found in the snapshot.</summary>
    NotFound,

    /// <summary>
    /// One of the flag's <see cref="Prerequisite"/>s did not resolve to a
    /// required variant. The default variant was returned instead of
    /// evaluating the flag's own rules (ADR-0027).
    /// </summary>
    PrerequisiteNotMet,
}
