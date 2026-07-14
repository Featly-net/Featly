using System.Text.Json;

namespace Featly.Server.Endpoints;

/// <summary>
/// Defense-in-depth caps on admin write payloads (issue #206). An authenticated
/// author with write permission could otherwise store a pathological definition
/// — thousands of variants/rules/conditions or a huge regex pattern — that
/// bloats every SDK snapshot and the engine's compiled-regex cache. Runtime
/// ReDoS is already mitigated (NonBacktracking), but nothing bounded the stored
/// size until now. Limits are generous; they exist to reject the absurd.
/// </summary>
internal static class WritePayloadLimits
{
    public const int MaxVariants = 50;
    public const int MaxRules = 200;
    public const int MaxConditionsPerRule = 50;
    public const int MaxPatternLength = 1000;

    /// <summary>Validates a flag write payload; returns an error message or <c>null</c> when acceptable.</summary>
    public static string? ValidateFlag(IReadOnlyList<Variant>? variants, IReadOnlyList<Rule>? rules)
    {
        if (variants is { Count: var vc } && vc > MaxVariants)
        {
            return $"A flag may have at most {MaxVariants} variants.";
        }
        if (rules is null)
        {
            return null;
        }
        if (rules.Count > MaxRules)
        {
            return $"A flag may have at most {MaxRules} rules.";
        }
        return rules.Select(rule => ValidateConditions(rule.Conditions)).FirstOrDefault(error => error is not null);
    }

    /// <summary>Validates a config write payload; returns an error message or <c>null</c> when acceptable.</summary>
    public static string? ValidateConfig(IReadOnlyList<ConfigRule>? rules)
    {
        if (rules is null)
        {
            return null;
        }
        if (rules.Count > MaxRules)
        {
            return $"A config may have at most {MaxRules} rules.";
        }
        return rules.Select(rule => ValidateConditions(rule.Conditions)).FirstOrDefault(error => error is not null);
    }

    /// <summary>Validates a condition set (rule or segment); returns an error message or <c>null</c> when acceptable.</summary>
    public static string? ValidateConditions(IReadOnlyList<Condition>? conditions)
    {
        if (conditions is null)
        {
            return null;
        }
        if (conditions.Count > MaxConditionsPerRule)
        {
            return $"A rule or segment may have at most {MaxConditionsPerRule} conditions.";
        }
        var hasOverlongPattern = conditions.Any(condition =>
            condition.Operator == ConditionOperator.Matches
            && condition.Value.ValueKind == JsonValueKind.String
            && (condition.Value.GetString()?.Length ?? 0) > MaxPatternLength);
        return hasOverlongPattern
            ? $"A regex ('Matches') condition pattern may be at most {MaxPatternLength} characters."
            : null;
    }
}
