using System.Text.Json;

namespace Featly.Server.Endpoints;

/// <summary>
/// Presence checks for admin write bodies. System.Text.Json binds a missing
/// non-nullable member to <c>null</c> / <c>default</c> without complaint, so a
/// body that omits <c>variants</c> or <c>defaultValue</c> used to reach the
/// handler and surface as a 500 (issue #324). Each write record calls these
/// from its <c>Validate()</c> and the handler turns a non-empty map into an
/// RFC 7807 validation problem via <see cref="Problems.Validation(IDictionary{string, string[]})"/>.
/// </summary>
internal static class WriteRequestValidation
{
    public static void Required(Dictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{field} is required."];
        }
    }

    public static void Required<T>(Dictionary<string, string[]> errors, string field, IReadOnlyList<T>? value)
    {
        if (value is null)
        {
            errors[field] = [$"{field} is required."];
        }
    }

    /// <summary>A <see cref="JsonElement"/> that was never bound has <see cref="JsonValueKind.Undefined"/>.</summary>
    public static void Required(Dictionary<string, string[]> errors, string field, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            errors[field] = [$"{field} is required."];
        }
    }

    public static Dictionary<string, string[]>? NullIfEmpty(Dictionary<string, string[]> errors) =>
        errors.Count == 0 ? null : errors;
}
