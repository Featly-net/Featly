using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Authentication;

/// <summary>
/// Options bag for the API-key bearer scheme.
/// </summary>
public sealed class FeatlyApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Static token clients may supply in the <c>Authorization: Bearer ...</c>
    /// header. This is the appsettings-configured bootstrap key; persisted
    /// <see cref="ApiKey"/> rows are also accepted (see the handler).
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Logical scope written into the resulting <see cref="ClaimsPrincipal"/>.</summary>
    public string Scope { get; set; } = "";
}

/// <summary>
/// API-key bearer authentication. A presented bearer token is matched, in order,
/// against:
/// <list type="number">
///   <item>the static <see cref="FeatlyApiKeyAuthenticationOptions.ApiKey"/>
///         (the appsettings bootstrap key) — constant-time compared; and</item>
///   <item>persisted <see cref="ApiKey"/> rows of this scheme's scope, located by
///         <see cref="ApiKey.Prefix"/> and verified with Argon2id.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// When a persisted key is <see cref="ApiKey.UserId">bound to a user</see>, the
/// issued principal carries that user's identifier — so RBAC, audit, and the
/// approval workflow attribute the request to a real person instead of an
/// anonymous <c>api-key:Scope</c> pseudo-identity. An unbound key authenticates
/// as a service principal named after the key.
/// </para>
/// <para>
/// The DB lookup only runs when a bearer token is present and the static key
/// did not match, so cookie requests (no <c>Authorization</c> header) and
/// bootstrap-key requests never pay the Argon2 cost.
/// </para>
/// </remarks>
public sealed class FeatlyApiKeyAuthenticationHandler(
    IOptionsMonitor<FeatlyApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    StorageFacade store)
    : AuthenticationHandler<FeatlyApiKeyAuthenticationOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        if (!AuthenticationHeaderValue.TryParse(headerValues.ToString(), out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header.Parameter;

        // 1. Static appsettings key (bootstrap / SDK quickstart). Fast path,
        //    constant-time compared to avoid a timing oracle on the token.
        var expected = Options.ApiKey;
        if (!string.IsNullOrEmpty(expected) && FixedTimeEquals(token, expected))
        {
            return Success(name: $"api-key:{Options.Scope}");
        }

        // 2. Persisted ApiKey of this scheme's scope: prefix lookup + Argon2 verify.
        if (Enum.TryParse<ApiKeyScope>(Options.Scope, ignoreCase: false, out var scope))
        {
            var ct = Context.RequestAborted;
            var prefix = ApiKeyHasher.ExtractPrefix(token);
            var candidates = await store.ApiKeys.FindCandidatesByPrefixAsync(prefix, ct).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                if (candidate.Scope != scope || !ApiKeyHasher.Verify(token, candidate.Hash))
                {
                    continue;
                }

                // Bound keys act as their user (real identity for RBAC + audit +
                // approvals); unbound keys are service principals named after the key.
                string name;
                if (candidate.UserId is Guid userId)
                {
                    var user = await store.Users.GetByIdAsync(userId, ct).ConfigureAwait(false);
                    if (user is null || user.Disabled)
                    {
                        return AuthenticateResult.Fail("The API key's bound user is missing or disabled.");
                    }
                    name = user.Identifier;
                }
                else
                {
                    name = candidate.Name;
                }

                // Best-effort last-used touch; never block auth on it.
                _ = store.ApiKeys.TouchLastUsedAsync(candidate.Id, DateTimeOffset.UtcNow, CancellationToken.None);
                return Success(name);
            }
        }

        // A bearer token was presented but matched nothing.
        return AuthenticateResult.Fail("Invalid API key.");
    }

    private AuthenticateResult Success(string name)
    {
        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.Name, name));
        identity.AddClaim(new Claim(FeatlyAuthenticationDefaults.ScopeClaim, Options.Scope));
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>Constant-time string comparison to avoid timing attacks on the static key.</summary>
    private static bool FixedTimeEquals(string a, string b)
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
