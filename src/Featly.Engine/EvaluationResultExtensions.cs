using System.Text.Json;

namespace Featly.Engine;

/// <summary>
/// Helpers for projecting the raw <see cref="JsonElement"/> value held by
/// an <see cref="EvaluationResult{JsonElement}"/> into the typed value
/// the caller asked for.
/// </summary>
public static class EvaluationResultExtensions
{
    /// <summary>
    /// Returns the value as <typeparamref name="T"/>. If conversion fails,
    /// returns <paramref name="fallback"/>.
    /// </summary>
    public static T As<T>(this EvaluationResult<JsonElement> result, T fallback)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            if (result.Reason == EvaluationReason.NotFound || result.Reason == EvaluationReason.Error)
            {
                return fallback;
            }

            return result.Value.ValueKind switch
            {
                JsonValueKind.Undefined or JsonValueKind.Null => fallback,
                _ => result.Value.Deserialize<T>() ?? fallback,
            };
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }
}
