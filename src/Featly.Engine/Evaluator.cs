using System.Text.Json;

namespace Featly.Engine;

/// <summary>
/// Pure, side-effect-free evaluation entry point. Shared between SDK
/// (local evaluation) and Server (preview / dry-run) so both reach the
/// same answer for the same inputs.
/// </summary>
/// <remarks>
/// M2 implements only the boolean-minimum slice: kill switch, archived
/// short-circuit, and default-variant fallback. Rules, conditions,
/// segment resolution, and weighted bucketing land in M3.
/// </remarks>
public static class Evaluator
{
    /// <summary>
    /// Evaluates <paramref name="flag"/> against <paramref name="context"/> and
    /// returns the chosen <see cref="EvaluationResult{T}"/>.
    /// </summary>
    public static EvaluationResult<JsonElement> EvaluateFlag(
        Flag? flag,
        EvaluationContext? context,
        JsonElement fallback)
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
            return ResolveDefault(flag, fallback, EvaluationReason.Disabled);
        }

        // M3: walk flag.Rules ordered by Order, returning the first match.
        // For now there are no rules, so we always fall through to the default.
        _ = context;

        return ResolveDefault(flag, fallback, EvaluationReason.Default);
    }

    private static EvaluationResult<JsonElement> ResolveDefault(
        Flag flag,
        JsonElement fallback,
        EvaluationReason reason)
    {
        var variant = FindVariant(flag, flag.DefaultVariantKey);
        if (variant is null)
        {
            return new EvaluationResult<JsonElement>(
                Key: flag.Key,
                Value: fallback,
                VariantKey: flag.DefaultVariantKey,
                Reason: EvaluationReason.Error,
                Error: $"Default variant '{flag.DefaultVariantKey}' is not declared on flag '{flag.Key}'.");
        }

        return new EvaluationResult<JsonElement>(
            Key: flag.Key,
            Value: variant.Value,
            VariantKey: variant.Key,
            Reason: reason);
    }

    private static Variant? FindVariant(Flag flag, string key)
    {
        foreach (var variant in flag.Variants)
        {
            if (string.Equals(variant.Key, key, StringComparison.Ordinal))
            {
                return variant;
            }
        }

        return null;
    }
}
