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

        if (addresses.Length == 0)
        {
            return true;
        }

        foreach (var address in addresses)
        {
            if (IsBlocked(address))
            {
                return false;
            }
        }
        return true;
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

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            // 10.0.0.0/8
            if (b[0] == 10) { return true; }
            // 172.16.0.0/12
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) { return true; }
            // 192.168.0.0/16
            if (b[0] == 192 && b[1] == 168) { return true; }
            // 169.254.0.0/16 (link-local, incl. 169.254.169.254 metadata)
            if (b[0] == 169 && b[1] == 254) { return true; }
            // 100.64.0.0/10 (carrier-grade NAT)
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) { return true; }
            // 0.0.0.0/8 and 255.255.255.255
            if (b[0] == 0 || address.Equals(IPAddress.Broadcast)) { return true; }
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast)
            {
                return true;
            }
            // Unspecified ::
            if (address.Equals(IPAddress.IPv6Any))
            {
                return true;
            }
            return false;
        }

        // Unknown address family — refuse.
        return true;
    }
}
