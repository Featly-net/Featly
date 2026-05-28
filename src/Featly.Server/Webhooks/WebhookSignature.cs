using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Featly.Server.Webhooks;

/// <summary>
/// Computes the HMAC-SHA256 signature sent in the <c>X-Featly-Signature</c>
/// header (GitHub-webhook style). Receivers recompute it over the raw request
/// body with the shared secret and compare to authenticate the delivery.
/// </summary>
internal static class WebhookSignature
{
    /// <summary>Header carrying the signature.</summary>
    public const string Header = "X-Featly-Signature";

    /// <summary>
    /// Returns <c>sha256=&lt;hex&gt;</c> for the UTF-8 bytes of <paramref name="payload"/>
    /// keyed by <paramref name="secret"/>.
    /// </summary>
    public static string Compute(string secret, string payload)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(payload);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }
}
