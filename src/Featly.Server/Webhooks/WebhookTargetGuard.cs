using System.Net;
using System.Net.Sockets;

namespace Featly.Server.Webhooks;

/// <summary>
/// SSRF guard for webhook targets (issue #189). Rejects endpoints that point at
/// the loopback interface, private/RFC1918 ranges, link-local (incl. the cloud
/// metadata address <c>169.254.169.254</c>), unique-local IPv6, or non-HTTP
/// schemes. Two layers: a cheap literal check at create/update time and a
/// DNS-resolving check at delivery time (which also defeats DNS rebinding).
/// Operators who intentionally target an internal receiver opt out via
/// <see cref="WebhookOptions.AllowPrivateNetworkTargets"/>.
/// </summary>
internal static class WebhookTargetGuard
{
    /// <summary>
    /// Cheap synchronous validation used at create/update: enforces an http(s)
    /// scheme and blocks obvious internal targets (IP literals in a blocked
    /// range, and the <c>localhost</c> hostname). Hostnames are only fully
    /// resolved at delivery time. Returns <c>true</c> when the URL is acceptable.
    /// </summary>
    public static bool IsAllowedAtWrite(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // If the host is an IP literal, classify it now; hostnames pass here and
        // are re-checked (with DNS) at delivery time.
        return !IPAddress.TryParse(uri.Host, out var literal) || !IsBlocked(literal);
    }

    /// <summary>
    /// DNS-resolving validation used at delivery time: every address the host
    /// resolves to must be outside the blocked ranges. Also covers IP literals.
    /// Returns <c>true</c> when delivery to <paramref name="uri"/> is acceptable.
    /// </summary>
    public static async Task<bool> IsAllowedAtDeliveryAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return !IsBlocked(literal);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // Unresolvable host — let the delivery attempt fail normally rather
            // than treating a DNS hiccup as a policy block.
            return true;
        }

        return addresses.Length == 0 || !addresses.Any(IsBlocked);
    }

    /// <summary>True when the address is loopback, private, link-local, unique-local, or otherwise not a valid public unicast target.</summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedIPv4(address),
            AddressFamily.InterNetworkV6 => IsBlockedIPv6(address),
            _ => true, // unknown address family — refuse
        };
    }

    private static bool IsBlockedIPv4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] switch
        {
            10 => true,                              // 10.0.0.0/8
            172 when b[1] is >= 16 and <= 31 => true, // 172.16.0.0/12
            192 when b[1] == 168 => true,            // 192.168.0.0/16
            169 when b[1] == 254 => true,            // 169.254.0.0/16 link-local (incl. 169.254.169.254 metadata)
            100 when b[1] is >= 64 and <= 127 => true, // 100.64.0.0/10 CGNAT
            0 => true,                               // 0.0.0.0/8
            _ => address.Equals(IPAddress.Broadcast), // 255.255.255.255
        };
    }

    private static bool IsBlockedIPv6(IPAddress address)
        => address.IsIPv6LinkLocal
        || address.IsIPv6UniqueLocal
        || address.IsIPv6Multicast
        || address.Equals(IPAddress.IPv6Any); // unspecified ::
}
