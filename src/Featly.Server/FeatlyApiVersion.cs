namespace Featly.Server;

/// <summary>
/// The HTTP API's version contract (ARCHITECTURE.md §8, issue #227). A client
/// pins the major version it was written against by sending
/// <see cref="RequestHeader"/>; every response echoes the version actually served
/// in <see cref="ResponseHeader"/>. Requests that pin an unsupported version are
/// refused with <c>406 Not Acceptable</c> rather than silently served a shape
/// they cannot parse.
/// </summary>
/// <remarks>
/// <para>
/// The point of shipping this before the API is declared stable is that a client
/// can only be protected from a future breaking change if it is <em>already</em>
/// pinning today. Introducing v2 against a population of unpinned clients has no
/// safe answer, so the contract lands first and the version list grows later.
/// </para>
/// <para>
/// Deprecation is announced per RFC 8594 (<c>Sunset</c>) alongside the IETF
/// <c>Deprecation</c> header: once a major version appears in
/// <see cref="Deprecated"/>, requests pinned to it keep working but every
/// response carries its retirement date.
/// </para>
/// </remarks>
public static class FeatlyApiVersion
{
    /// <summary>Request header a client pins its major version with (e.g. <c>Accept-Version: 1</c>).</summary>
    public const string RequestHeader = "Accept-Version";

    /// <summary>Response header echoing the major version actually served.</summary>
    public const string ResponseHeader = "Featly-Version";

    /// <summary>The major version served when a request pins nothing.</summary>
    public const string Current = "1";

    /// <summary>Major versions this server still answers.</summary>
    public static readonly IReadOnlyList<string> Supported = [Current];

    /// <summary>
    /// Supported-but-deprecated majors mapped to their sunset date. Empty while
    /// only one version exists; adding an entry starts the <c>Deprecation</c> /
    /// <c>Sunset</c> announcements without any other code change.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, DateTimeOffset> Deprecated =
        new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

    /// <summary>
    /// Reduces a pinned version to its major token: <c>"1"</c>, <c>"1.4"</c> and
    /// <c>"v1"</c> all pin major <c>1</c>. Returns <c>null</c> when the value is
    /// not a version at all.
    /// </summary>
    public static string? Major(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var token = requested.Trim();
        if (token.StartsWith('v') || token.StartsWith('V'))
        {
            token = token[1..];
        }

        var dot = token.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            token = token[..dot];
        }

        return token.Length > 0 && token.All(char.IsAsciiDigit) ? token : null;
    }
}
