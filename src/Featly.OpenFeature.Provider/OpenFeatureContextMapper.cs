using System.Text.Json;
using OpenFeature.Model;
using FeatlyContext = Featly.EvaluationContext;

namespace Featly.OpenFeature;

/// <summary>
/// Translates between OpenFeature and Featly evaluation types: the OpenFeature
/// <see cref="EvaluationContext"/> into Featly's <see cref="FeatlyContext"/>,
/// and a Featly <see cref="JsonElement"/> result into an OpenFeature
/// <see cref="Value"/> (for structure resolution).
/// </summary>
internal static class OpenFeatureContextMapper
{
    /// <summary>
    /// Maps an OpenFeature context to Featly's. The OpenFeature targeting key
    /// becomes <see cref="FeatlyContext.TargetingKey"/>; every other attribute
    /// is flattened into <see cref="FeatlyContext.Attributes"/> as plain CLR
    /// values the engine can match on. Returns <c>null</c> when there is nothing
    /// to carry (so the SDK falls back to its ambient context, if any).
    /// </summary>
    public static FeatlyContext? ToFeatly(global::OpenFeature.Model.EvaluationContext? context)
    {
        if (context is null)
        {
            return null;
        }

        Dictionary<string, object?>? attributes = null;
        foreach (var kv in context.AsDictionary())
        {
            attributes ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            attributes[kv.Key] = ToClr(kv.Value);
        }

        var targetingKey = context.TargetingKey;
        if (string.IsNullOrEmpty(targetingKey) && attributes is null)
        {
            return null;
        }

        return new FeatlyContext(
            string.IsNullOrEmpty(targetingKey) ? null : targetingKey,
            attributes);
    }

    /// <summary>Unwraps an OpenFeature <see cref="Value"/> into a plain CLR object.</summary>
    private static object? ToClr(Value value)
    {
        if (value is null || value.IsNull)
        {
            return null;
        }
        if (value.IsBoolean)
        {
            return value.AsBoolean;
        }
        if (value.IsNumber)
        {
            return value.AsDouble;
        }
        if (value.IsString)
        {
            return value.AsString;
        }
        if (value.IsDateTime)
        {
            return value.AsDateTime;
        }
        if (value.IsList)
        {
            return value.AsList!.Select(ToClr).ToList();
        }
        if (value.IsStructure)
        {
            var nested = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in value.AsStructure!.AsDictionary())
            {
                nested[kv.Key] = ToClr(kv.Value);
            }
            return nested;
        }
        return value.AsObject;
    }

    /// <summary>Converts a Featly flag value (raw JSON) into an OpenFeature <see cref="Value"/>.</summary>
    public static Value ToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => new Value(true),
        JsonValueKind.False => new Value(false),
        JsonValueKind.String => new Value(element.GetString() ?? string.Empty),
        JsonValueKind.Number => new Value(element.GetDouble()),
        JsonValueKind.Array => new Value(element.EnumerateArray().Select(ToValue).ToList<Value>()),
        JsonValueKind.Object => new Value(ToStructure(element)),
        _ => new Value(),
    };

    private static Structure ToStructure(JsonElement element)
    {
        var dict = new Dictionary<string, Value>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = ToValue(property.Value);
        }
        return new Structure(dict);
    }
}
