using System.Text.Json;
using Featly.Engine.Internal;

namespace Featly.Engine;

/// <summary>
/// Pure, side-effect-free evaluation entry point. Shared between SDK
/// (local evaluation) and Server (preview / dry-run) so both reach the
/// same answer for the same inputs.
/// </summary>
/// <remarks>
/// M3 implements the full algorithm from ARCHITECTURE.md §5:
/// <list type="number">
///   <item>Null / archived → fallback with reason <c>NotFound</c>.</item>
///   <item>Disabled → default variant with reason <c>Disabled</c>.</item>
///   <item>Walk rules ordered by <see cref="Rule.Order"/> asc, skip disabled, first whose
///         conditions all match (AND) wins.</item>
///   <item>If the matched rule's outcome is a single variant → return it with reason
///         <c>TargetingMatch</c>.</item>
///   <item>If the outcome is a split → MurmurHash3 bucket the targeting key into the
///         cumulative weights, return the picked variant with reason <c>Split</c>.</item>
///   <item>No rule matches → default variant with reason <c>Default</c>.</item>
/// </list>
/// </remarks>
public static class Evaluator
{
    /// <summary>
    /// Evaluates <paramref name="flag"/> against <paramref name="context"/>
    /// using <paramref name="segments"/> to resolve <c>InSegment</c> conditions.
    /// </summary>
    public static EvaluationResult<JsonElement> EvaluateFlag(
        Flag? flag,
        EvaluationContext? context,
        JsonElement fallback,
        ISegmentLookup? segments = null)
    {
        if (flag is null)
        {
            return new EvaluationResult<JsonElement>(
                Key: "",
                Value: fallback,
                VariantKey: "",
                Reason: EvaluationReason.NotFound);
        }

        if (flag.Archived)
        {
            return new EvaluationResult<JsonElement>(
                Key: flag.Key,
                Value: fallback,
                VariantKey: "",
                Reason: EvaluationReason.NotFound);
        }

        if (!flag.Enabled)
        {
            return ResolveDefault(flag, fallback, EvaluationReason.Disabled, ruleMatched: null);
        }

        segments ??= DictionarySegmentLookup.Empty;

        // Walk rules ordered by Order asc. We don't sort here on the hot path —
        // the storage layer / API layer is responsible for keeping the in-memory
        // list ordered. A defensive sort is cheap if a future regression sneaks
        // in; for now we trust the source.
        for (var i = 0; i < flag.Rules.Count; i++)
        {
            var rule = flag.Rules[i];
            if (!rule.Enabled)
            {
                continue;
            }

            if (!AllConditionsMatch(rule.Conditions, context, segments))
            {
                continue;
            }

            return ApplyRuleOutcome(flag, rule, context, fallback);
        }

        return ResolveDefault(flag, fallback, EvaluationReason.Default, ruleMatched: null);
    }

    private static bool AllConditionsMatch(
        List<Condition> conditions,
        EvaluationContext? context,
        ISegmentLookup segments)
    {
        for (var i = 0; i < conditions.Count; i++)
        {
            if (!ConditionEvaluator.Matches(conditions[i], context, segments))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Evaluates a dynamic <see cref="Config"/> against <paramref name="context"/>
    /// using the same rule + condition + segment machinery as flags. The outcome
    /// is the value served directly (no variant indirection); the engine walks
    /// <see cref="Config.Rules"/> ordered by <see cref="ConfigRule.Order"/> asc
    /// and returns the first match's <see cref="ConfigRule.Value"/>. Returns
    /// <see cref="Config.DefaultValue"/> when no rule matches.
    /// </summary>
    public static EvaluationResult<JsonElement> EvaluateConfig(
        Config? config,
        EvaluationContext? context,
        JsonElement fallback,
        ISegmentLookup? segments = null)
    {
        if (config is null)
        {
            return new EvaluationResult<JsonElement>(
                Key: "",
                Value: fallback,
                VariantKey: "",
                Reason: EvaluationReason.NotFound);
        }

        if (config.Archived)
        {
            return new EvaluationResult<JsonElement>(
                Key: config.Key,
                Value: fallback,
                VariantKey: "",
                Reason: EvaluationReason.NotFound);
        }

        segments ??= DictionarySegmentLookup.Empty;

        for (var i = 0; i < config.Rules.Count; i++)
        {
            var rule = config.Rules[i];
            if (!rule.Enabled)
            {
                continue;
            }

            if (!AllConditionsMatch(rule.Conditions, context, segments))
            {
                continue;
            }

            return new EvaluationResult<JsonElement>(
                Key: config.Key,
                Value: rule.Value,
                VariantKey: "",
                Reason: EvaluationReason.TargetingMatch,
                RuleMatched: rule.Name);
        }

        // No rule matched — serve the default value.
        return new EvaluationResult<JsonElement>(
            Key: config.Key,
            Value: config.DefaultValue,
            VariantKey: "",
            Reason: EvaluationReason.Default);
    }

    private static EvaluationResult<JsonElement> ApplyRuleOutcome(
        Flag flag,
        Rule rule,
        EvaluationContext? context,
        JsonElement fallback)
    {
        var outcome = rule.Outcome;

        // Deterministic variant selection.
        if (!string.IsNullOrWhiteSpace(outcome.VariantKey))
        {
            var variant = FindVariant(flag, outcome.VariantKey!);
            if (variant is null)
            {
                return UnresolvedVariant(flag, outcome.VariantKey!, fallback);
            }

            return new EvaluationResult<JsonElement>(
                Key: flag.Key,
                Value: variant.Value,
                VariantKey: variant.Key,
                Reason: EvaluationReason.TargetingMatch,
                RuleMatched: rule.Name);
        }

        // Weighted bucketing.
        if (outcome.Splits is { Count: > 0 } splits)
        {
            // Bucketing needs a targeting key. Without one, fall through to the
            // default variant — the engine never throws.
            var targetingKey = context?.TargetingKey;
            if (string.IsNullOrWhiteSpace(targetingKey))
            {
                return ResolveDefault(flag, fallback, EvaluationReason.Default, ruleMatched: rule.Name);
            }

            var picked = Bucketer.PickSplit(targetingKey, flag.Key, splits);
            if (picked is null)
            {
                return ResolveDefault(flag, fallback, EvaluationReason.Default, ruleMatched: rule.Name);
            }

            var variant = FindVariant(flag, picked.VariantKey);
            if (variant is null)
            {
                return UnresolvedVariant(flag, picked.VariantKey, fallback);
            }

            return new EvaluationResult<JsonElement>(
                Key: flag.Key,
                Value: variant.Value,
                VariantKey: variant.Key,
                Reason: EvaluationReason.Split,
                RuleMatched: rule.Name);
        }

        // Outcome carried neither a variant key nor splits — treat as misconfigured.
        return new EvaluationResult<JsonElement>(
            Key: flag.Key,
            Value: fallback,
            VariantKey: "",
            Reason: EvaluationReason.Error,
            RuleMatched: rule.Name,
            Error: $"Rule '{rule.Name ?? rule.Id.ToString()}' on flag '{flag.Key}' has no outcome.");
    }

    private static EvaluationResult<JsonElement> ResolveDefault(
        Flag flag,
        JsonElement fallback,
        EvaluationReason reason,
        string? ruleMatched)
    {
        var variant = FindVariant(flag, flag.DefaultVariantKey);
        if (variant is null)
        {
            return UnresolvedVariant(flag, flag.DefaultVariantKey, fallback);
        }

        return new EvaluationResult<JsonElement>(
            Key: flag.Key,
            Value: variant.Value,
            VariantKey: variant.Key,
            Reason: reason,
            RuleMatched: ruleMatched);
    }

    private static EvaluationResult<JsonElement> UnresolvedVariant(Flag flag, string missingKey, JsonElement fallback)
        => new(
            Key: flag.Key,
            Value: fallback,
            VariantKey: missingKey,
            Reason: EvaluationReason.Error,
            Error: $"Variant '{missingKey}' is not declared on flag '{flag.Key}'.");

    private static Variant? FindVariant(Flag flag, string key)
    {
        for (var i = 0; i < flag.Variants.Count; i++)
        {
            if (string.Equals(flag.Variants[i].Key, key, StringComparison.Ordinal))
            {
                return flag.Variants[i];
            }
        }
        return null;
    }
}
