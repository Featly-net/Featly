using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Featly.Storage;

/// <summary>
/// Computes the tamper-evident hash for an <see cref="AuditEntry"/> in the audit
/// hash chain (issue #208). Each entry's <see cref="AuditEntry.Hash"/> is a
/// SHA-256 over its immutable content plus the previous entry's hash, so altering
/// any field or removing a link is detectable by recomputation.
/// </summary>
public static class AuditHash
{
    /// <summary>
    /// Returns the lowercase-hex SHA-256 chaining <paramref name="entry"/> onto
    /// <paramref name="previousHash"/>. Deterministic: the fields are folded in a
    /// fixed order with an unambiguous separator, and <see cref="AuditEntry.Data"/>
    /// uses its raw JSON text (stable across the store's round-trip).
    /// </summary>
    public static string Compute(AuditEntry entry, string? previousHash)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Length-prefixed fields so no combination of values can be rearranged
        // into the same byte stream (a simple delimiter could collide if a field
        // contained the delimiter).
        var sb = new StringBuilder();
        Append(sb, previousHash);
        Append(sb, entry.Sequence.ToString(CultureInfo.InvariantCulture));
        Append(sb, entry.Id.ToString("N"));
        Append(sb, entry.At.UtcTicks.ToString(CultureInfo.InvariantCulture));
        Append(sb, entry.Action);
        Append(sb, entry.EntityType);
        Append(sb, entry.EntityKey);
        Append(sb, entry.EnvironmentId?.ToString("N"));
        Append(sb, entry.ActorIdentifier);
        Append(sb, entry.Data?.GetRawText());

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(digest);
    }

    private static void Append(StringBuilder sb, string? value)
    {
        if (value is null)
        {
            sb.Append("-1:");
            return;
        }

        sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }
}
