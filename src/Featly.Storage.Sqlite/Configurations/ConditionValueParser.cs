using System.Text.Json;

namespace Featly.Storage.Sqlite.Configurations;

/// <summary>
/// Round-trips a <see cref="System.Text.Json.JsonElement"/> through raw JSON
/// text. EF Core's owned JSON columns serialise the parent entity as a single
/// JSON document; storing a nested <c>JsonElement</c> verbatim requires
/// converting it to text and parsing back on read.
/// </summary>
internal static class ConditionValueParser
{
    public static JsonElement ParseJsonElement(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        using var doc = JsonDocument.Parse(text);
        // Clone detaches from the JsonDocument lifetime so it survives the using block.
        return doc.RootElement.Clone();
    }
}
