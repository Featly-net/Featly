using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Featly.Storage.MySql.Configurations;

/// <summary>
/// Maps a <c>List&lt;T&gt;</c>-typed property to a native MySQL <c>json</c>
/// column via <see cref="System.Text.Json"/> serialization, with a value
/// comparer so EF Core's change tracking sees list-content changes rather than
/// comparing by reference.
/// </summary>
/// <remarks>
/// Every other relational provider maps <c>Flag.Variants</c>/<c>Rules</c> and
/// similar collections as an EF Core owned-entity JSON document
/// (<c>OwnsMany(...).ToJson()</c>), a feature Pomelo does not yet implement —
/// see <a href="https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/1752">
/// PomeloFoundation/Pomelo.EntityFrameworkCore.MySql#1752</a> (targeted at
/// Pomelo's own next major version, which does not exist at time of writing).
/// This is the fallback: a plain scalar property holding the whole list
/// serialized as JSON text, with <c>HasColumnType("json")</c> so MySQL still
/// validates and can query into it — semantically equivalent to
/// <c>ToJson()</c> for our access pattern (always load/replace the whole
/// list with the parent row), just without EF's per-element change tracking.
/// </remarks>
internal static class JsonCollectionConversion
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Configures this list property as a JSON-column-backed collection of <typeparamref name="T"/>.</summary>
    public static void AsJsonColumn<T>(this PropertyBuilder<List<T>> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.HasColumnType("json");
        property.HasConversion(
            v => JsonSerializer.Serialize(v, SerializerOptions),
            v => Deserialize<T>(v));
        property.Metadata.SetValueComparer(new ValueComparer<List<T>>(
            (a, b) => JsonEquals(a, b),
            v => JsonSerializer.Serialize(v, SerializerOptions).GetHashCode(StringComparison.Ordinal),
            v => Clone(v)));
    }

    private static List<T> Deserialize<T>(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : JsonSerializer.Deserialize<List<T>>(text, SerializerOptions) ?? [];

    private static List<T> Clone<T>(List<T> value) =>
        JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(value, SerializerOptions), SerializerOptions) ?? [];

    private static bool JsonEquals<T>(List<T>? a, List<T>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return JsonSerializer.Serialize(a, SerializerOptions) == JsonSerializer.Serialize(b, SerializerOptions);
    }
}
