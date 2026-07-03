using System.Text.Json;

namespace Featly.Storage.Postgres.Configurations;

/// <summary>
/// JSON round-trip helper for <see cref="Role.Permissions"/>. Storing the
/// string names of each <see cref="Permission"/> (instead of the int values)
/// makes the on-disk format stable across future enum reorderings — adding
/// a new permission in the middle of the enum will not silently re-shuffle
/// any saved role's contents. Duplicated from the SQLite provider's own
/// internal helper (ADR-0026: each provider owns its configuration types).
/// </summary>
internal static class PermissionListSerializer
{
    public static string Serialize(List<Permission> perms)
    {
        var names = new string[perms.Count];
        for (var i = 0; i < perms.Count; i++)
        {
            names[i] = perms[i].ToString();
        }
        return JsonSerializer.Serialize(names);
    }

    public static List<Permission> Deserialize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        var names = JsonSerializer.Deserialize<string[]>(text) ?? [];
        // Unknown names (a permission removed in a later release) are silently
        // dropped — the role simply doesn't grant that permission anymore.
        return [.. names
            .Where(static n => Enum.TryParse<Permission>(n, ignoreCase: false, out _))
            .Select(static n => Enum.Parse<Permission>(n, ignoreCase: false))];
    }
}
