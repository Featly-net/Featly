using System.Text.Json;

namespace Featly.Server.Endpoints;

/// <summary>
/// Presence checks for admin write bodies. System.Text.Json binds a missing
/// non-nullable member to <c>null</c> / <c>default</c> without complaint, so a
/// body that omits <c>variants</c> or <c>defaultValue</c> used to reach the
/// handler and surface as a 500 (issue #324). Each write record builds its
/// checks with this fluent helper and the handler turns a non-empty result into
/// an RFC 7807 validation problem via <see cref="Problems.Validation(IDictionary{string, string[]})"/>.
/// </summary>
internal sealed class WriteRequestValidation
{
    private readonly Dictionary<string, string[]> _errors = [];

    public static WriteRequestValidation Begin() => new();

    /// <summary>A string member that must be present and not blank.</summary>
    public WriteRequestValidation Text(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Missing(field);
        }

        return this;
    }

    /// <summary>A collection member that must be present (it may be empty).</summary>
    public WriteRequestValidation List<T>(string field, IReadOnlyList<T>? value)
    {
        if (value is null)
        {
            Missing(field);
        }

        return this;
    }

    /// <summary>A <see cref="JsonElement"/> that was never bound has <see cref="JsonValueKind.Undefined"/>.</summary>
    public WriteRequestValidation Json(string field, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            Missing(field);
        }

        return this;
    }

    /// <summary>The collected field errors, or <c>null</c> when the body is complete.</summary>
    public Dictionary<string, string[]>? Result() => _errors.Count == 0 ? null : _errors;

    private void Missing(string field) => _errors[field] = [$"{field} is required."];
}
