namespace Featly.Engine.Internal;

/// <summary>
/// Resolves an attribute path (e.g. <c>user.country</c>) against an
/// <see cref="EvaluationContext"/>. The path is treated as a literal key
/// into <see cref="EvaluationContext.Attributes"/>; the special path
/// <c>targetingKey</c> resolves to <see cref="EvaluationContext.TargetingKey"/>.
/// </summary>
/// <remarks>
/// Matching the LaunchDarkly / Unleash convention here: dot-paths are
/// flat keys, not nested traversals. Authors pre-flatten their context
/// (<c>"user.country": "BR"</c>) rather than handing the engine deeply
/// nested objects.
/// </remarks>
internal static class AttributeResolver
{
    public const string TargetingKeyAttribute = "targetingKey";

    public static bool TryResolve(EvaluationContext? context, string attribute, out object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);

        if (context is null)
        {
            value = null;
            return false;
        }

        if (string.Equals(attribute, TargetingKeyAttribute, StringComparison.Ordinal))
        {
            value = context.TargetingKey;
            return value is not null;
        }

        if (context.Attributes is null)
        {
            value = null;
            return false;
        }

        return context.Attributes.TryGetValue(attribute, out value) && value is not null;
    }
}
