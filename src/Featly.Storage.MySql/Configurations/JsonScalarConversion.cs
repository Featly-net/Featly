using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

/// <summary>
/// Maps a bare <see cref="JsonElement"/>-typed property to a native MySQL
/// <c>json</c> column via raw-text round-tripping.
/// </summary>
/// <remarks>
/// Unlike <see cref="JsonCollectionConversion.AsJsonColumn{T}"/>'s
/// <c>List&lt;T&gt;</c> case, EF Core has no built-in converter for a
/// standalone <see cref="JsonElement"/> scalar — every relational provider
/// here needs this explicit conversion, not just the ones working around
/// Pomelo's missing <c>OwnsMany(...).ToJson()</c> support.
/// </remarks>
internal static class JsonScalarConversion
{
    /// <summary>Configures this property as a JSON-column-backed <see cref="JsonElement"/> scalar.</summary>
    public static void AsJsonColumn(this PropertyBuilder<JsonElement> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.HasColumnType("json");
        property.HasConversion(
            static value => value.GetRawText(),
            static text => Parse(text));
    }

    private static JsonElement Parse(string text)
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
