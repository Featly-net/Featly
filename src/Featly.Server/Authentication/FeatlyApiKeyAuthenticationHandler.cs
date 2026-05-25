using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Featly.Server.Authentication;

/// <summary>
/// Options bag for the static API-key bearer scheme.
/// </summary>
public sealed class FeatlyApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>Token clients must supply in the <c>Authorization: Bearer ...</c> header.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Logical scope written into the resulting <see cref="ClaimsPrincipal"/>.</summary>
    public string Scope { get; set; } = "";
}

/// <summary>
/// Static-token bearer authentication. Compares the bearer token supplied in
/// the request against the configured <see cref="FeatlyApiKeyAuthenticationOptions.ApiKey"/>
/// and, if equal, issues a <see cref="ClaimsPrincipal"/> tagged with the scope.
/// </summary>
/// <remarks>
/// Intentionally simple. Real per-environment <c>ApiKey</c> entities with
/// Argon2id hashing and scope enforcement land in M6. Used as the M2
/// bootstrap so the admin / SDK API surfaces have a real auth boundary
/// without dragging the full RBAC stack online.
/// </remarks>
public sealed class FeatlyApiKeyAuthenticationHandler(
    IOptionsMonitor<FeatlyApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<FeatlyApiKeyAuthenticationOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = Options.ApiKey;
        if (string.IsNullOrEmpty(expected))
        {
            // No key configured = scheme cannot authenticate anything.
            // This is the right behavior when the operator forgot to set the key.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!AuthenticationHeaderValue.TryParse(headerValues.ToString(), out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!StringComparerOrdinal.Equals(header.Parameter, expected))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.Name, $"api-key:{Options.Scope}"));
        identity.AddClaim(new Claim(FeatlyAuthenticationDefaults.ScopeClaim, Options.Scope));
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    /// <summary>Constant-time string comparison to avoid timing attacks on the static key.</summary>
    private static class StringComparerOrdinal
    {
        public static bool Equals(string a, string b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            var diff = 0;
            for (var i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }
    }
}
