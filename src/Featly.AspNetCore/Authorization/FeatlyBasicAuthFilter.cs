using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Featly.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Featly.AspNetCore.Authorization;

/// <summary>
/// Quickstart authorization filter that reads a username + password from
/// configuration and accepts the request when the incoming
/// <c>Authorization: Basic</c> header matches. Intended for local dev and
/// demos; production deployments should plug in a real auth scheme
/// (<see cref="IFeatlyDashboardAuthorizationFilter"/>).
/// </summary>
/// <remarks>
/// Comparisons are constant-time via <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
/// Configuration binds from <c>Featly:Auth:Basic</c>.
/// </remarks>
public sealed class FeatlyBasicAuthFilter(IOptions<FeatlyBasicAuthOptions> options) : IFeatlyDashboardAuthorizationFilter
{
    private const string Scheme = "Basic ";

    private readonly FeatlyBasicAuthOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public Task<ResolvedUser?> AuthorizeAsync(HttpContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(_options.Username) || string.IsNullOrEmpty(_options.Password))
        {
            // Not configured — refuse rather than auto-allow.
            return Task.FromResult<ResolvedUser?>(null);
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return Task.FromResult<ResolvedUser?>(null);
        }

        string decoded;
        try
        {
            var bytes = Convert.FromBase64String(header[Scheme.Length..]);
            decoded = Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return Task.FromResult<ResolvedUser?>(null);
        }

        var split = decoded.IndexOf(':', StringComparison.Ordinal);
        if (split <= 0)
        {
            return Task.FromResult<ResolvedUser?>(null);
        }

        var username = decoded[..split];
        var password = decoded[(split + 1)..];

        var expectedUser = Encoding.UTF8.GetBytes(_options.Username);
        var expectedPass = Encoding.UTF8.GetBytes(_options.Password);
        var actualUser = Encoding.UTF8.GetBytes(username);
        var actualPass = Encoding.UTF8.GetBytes(password);

        // Constant-time comparison even when lengths differ — FixedTimeEquals
        // returns false fast for length mismatches without leaking timing.
        var ok = CryptographicOperations.FixedTimeEquals(expectedUser, actualUser)
              && CryptographicOperations.FixedTimeEquals(expectedPass, actualPass);
        if (!ok)
        {
            return Task.FromResult<ResolvedUser?>(null);
        }

        return Task.FromResult<ResolvedUser?>(new ResolvedUser(
            Identifier: _options.Username,
            DisplayName: string.IsNullOrEmpty(_options.DisplayName) ? _options.Username : _options.DisplayName));
    }
}

/// <summary>
/// Options for <see cref="FeatlyBasicAuthFilter"/>. Binds from the
/// <c>Featly:Auth:Basic</c> configuration section.
/// </summary>
public sealed class FeatlyBasicAuthOptions
{
    /// <summary>Configuration section name (<c>Featly:Auth:Basic</c>).</summary>
    public const string SectionName = "Featly:Auth:Basic";

    /// <summary>Username the filter accepts.</summary>
    public string Username { get; set; } = "";

    /// <summary>Password the filter accepts.</summary>
    public string Password { get; set; } = "";

    /// <summary>Friendly name to set on the resolved user; defaults to <see cref="Username"/> when empty.</summary>
    public string DisplayName { get; set; } = "";
}
