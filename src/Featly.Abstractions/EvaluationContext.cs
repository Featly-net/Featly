namespace Featly;

/// <summary>
/// Inputs supplied at evaluation time. The <see cref="TargetingKey"/> identifies
/// the subject being evaluated (a user id, a session id, a service name) and is
/// used by the engine for deterministic bucketing. <see cref="Attributes"/> carry
/// arbitrary properties that targeting rules can match on.
/// </summary>
/// <remarks>
/// Placeholder type for M1. Full semantics land with the engine in M3.
/// </remarks>
public sealed record EvaluationContext(
    string? TargetingKey = null,
    IReadOnlyDictionary<string, object?>? Attributes = null);
