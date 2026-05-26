using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Featly.Engine.Internal;

/// <summary>
/// Matches a single <see cref="Condition"/> against an
/// <see cref="EvaluationContext"/>. Returns <c>true</c> when the predicate
/// holds. <see cref="Condition.Negate"/> is applied at the end.
/// </summary>
/// <remarks>
/// Pure logic, no allocations on the hot path beyond what the underlying
/// JsonElement / Regex APIs do themselves. Regex matches are bounded by a
/// short timeout to avoid catastrophic backtracking on attacker-controlled
/// patterns.
/// </remarks>
internal static class ConditionEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    public static bool Matches(
        Condition condition,
        EvaluationContext? context,
        ISegmentLookup segments)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(segments);

        var raw = MatchesCore(condition, context, segments);
        return condition.Negate ? !raw : raw;
    }

    private static bool MatchesCore(
        Condition condition,
        EvaluationContext? context,
        ISegmentLookup segments)
    {
        // InSegment is special: the "attribute" is the segment key inside
        // condition.Value; recurse into the segment's own conditions.
        if (condition.Operator == ConditionOperator.InSegment)
        {
            if (condition.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var segmentKey = condition.Value.GetString();
            if (string.IsNullOrWhiteSpace(segmentKey))
            {
                return false;
            }

            if (!segments.TryGet(segmentKey, out var segment) || segment is null)
            {
                return false;
            }

            // AND across the segment's conditions, same semantics as a rule.
            foreach (var inner in segment.Conditions)
            {
                if (!Matches(inner, context, segments))
                {
                    return false;
                }
            }
            return true;
        }

        if (!AttributeResolver.TryResolve(context, condition.Attribute, out var actual) || actual is null)
        {
            return false;
        }

        return condition.Operator switch
        {
            ConditionOperator.Equals => EqualsScalar(actual, condition.Value),
            ConditionOperator.NotEquals => !EqualsScalar(actual, condition.Value),
            ConditionOperator.In => InArray(actual, condition.Value),
            ConditionOperator.NotIn => !InArray(actual, condition.Value),
            ConditionOperator.GreaterThan => CompareNumeric(actual, condition.Value) is int cmp && cmp > 0,
            ConditionOperator.GreaterThanOrEqual => CompareNumeric(actual, condition.Value) is int cmpGe && cmpGe >= 0,
            ConditionOperator.LessThan => CompareNumeric(actual, condition.Value) is int cmpLt && cmpLt < 0,
            ConditionOperator.LessThanOrEqual => CompareNumeric(actual, condition.Value) is int cmpLte && cmpLte <= 0,
            ConditionOperator.Contains => StringOp(actual, condition.Value, static (a, b) => a.Contains(b, StringComparison.Ordinal)),
            ConditionOperator.StartsWith => StringOp(actual, condition.Value, static (a, b) => a.StartsWith(b, StringComparison.Ordinal)),
            ConditionOperator.EndsWith => StringOp(actual, condition.Value, static (a, b) => a.EndsWith(b, StringComparison.Ordinal)),
            ConditionOperator.Matches => RegexMatch(actual, condition.Value),
            ConditionOperator.SemverGt => CompareSemver(actual, condition.Value) is int sg && sg > 0,
            ConditionOperator.SemverLt => CompareSemver(actual, condition.Value) is int sl && sl < 0,
            ConditionOperator.SemverEq => CompareSemver(actual, condition.Value) is int se && se == 0,
            _ => false,
        };
    }

    // -------- helpers --------

    private static bool EqualsScalar(object actual, JsonElement expected)
    {
        if (expected.ValueKind == JsonValueKind.String)
        {
            return string.Equals(AsString(actual), expected.GetString(), StringComparison.Ordinal);
        }

        if (expected.ValueKind is JsonValueKind.Number)
        {
            return AsDouble(actual) is double d && expected.TryGetDouble(out var e) && d.Equals(e);
        }

        if (expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return AsBool(actual) is bool b && b == (expected.ValueKind == JsonValueKind.True);
        }

        return false;
    }

    private static bool InArray(object actual, JsonElement expected)
    {
        if (expected.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var element in expected.EnumerateArray())
        {
            if (EqualsScalar(actual, element))
            {
                return true;
            }
        }
        return false;
    }

    private static int? CompareNumeric(object actual, JsonElement expected)
    {
        var a = AsDouble(actual);
        if (a is null || expected.ValueKind != JsonValueKind.Number || !expected.TryGetDouble(out var e))
        {
            return null;
        }
        return a.Value.CompareTo(e);
    }

    private static bool StringOp(object actual, JsonElement expected, Func<string, string, bool> op)
    {
        var a = AsString(actual);
        if (a is null || expected.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        var e = expected.GetString();
        return e is not null && op(a, e);
    }

    private static bool RegexMatch(object actual, JsonElement expected)
    {
        var a = AsString(actual);
        if (a is null || expected.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            var pattern = expected.GetString();
            if (string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            // RegexOptions.NonBacktracking would be safer, but it doesn't support all features.
            // Stick with a short timeout — same approach LaunchDarkly's .NET SDK uses.
            return Regex.IsMatch(a, pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid regex.
            return false;
        }
    }

    private static int? CompareSemver(object actual, JsonElement expected)
    {
        if (AsString(actual) is not string a || expected.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var e = expected.GetString();
        if (string.IsNullOrEmpty(e))
        {
            return null;
        }

        return SemverComparer.TryCompare(a, e, out var cmp) ? cmp : (int?)null;
    }

    // -------- attribute coercion --------

    private static string? AsString(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.ToString(),
        _ => value?.ToString(),
    };

    private static double? AsDouble(object value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        short sh => sh,
        byte by => by,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
        JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var jed) => jed,
        _ => null,
    };

    private static bool? AsBool(object value) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var p) => p,
        JsonElement je when je.ValueKind == JsonValueKind.True => true,
        JsonElement je when je.ValueKind == JsonValueKind.False => false,
        _ => null,
    };
}
