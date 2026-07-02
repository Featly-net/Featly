using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Featly.Server.Authentication;

/// <summary>
/// Synchronizer-token CSRF layer for the dashboard session (SECURITY_AUDIT.md
/// follow-up). Login mints a random per-session token, stores it as a claim
/// inside the <c>HttpOnly</c> auth cookie, and returns it to the dashboard;
/// this filter — attached once at the <c>/api</c> group root — requires every
/// mutating request whose <em>only</em> authentication is that cookie to echo
/// the token in the <c>X-Featly-Csrf</c> header.
/// </summary>
/// <remarks>
/// <para>
/// Defense in depth on top of <c>SameSite=Strict</c>: a cross-site attacker can
/// neither read the claim (the cookie is <c>HttpOnly</c>, the token only ever
/// travels in a JSON body to the same origin) nor set a custom header on a
/// cross-origin form post. Bearer-authenticated requests are exempt — a header
/// credential is not ambiently attached by browsers, so it is not CSRF-exposed
/// (and requiring a second header would break every script). Anonymous
/// requests (login, health) pass through untouched; the endpoint's own
/// authorization still applies.
/// </para>
/// <para>
/// Sessions minted before this layer existed carry no token claim and fail
/// closed with a hint to sign in again.
/// </para>
/// </remarks>
internal sealed class FeatlyCsrfFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (IsSafeMethod(http.Request.Method))
        {
            return await next(context).ConfigureAwait(false);
        }

        var identities = http.User?.Identities;
        if (identities is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        // Only enforce when the cookie is the sole authenticated identity: a
        // Bearer credential is header-borne and not ambient, so it already
        // proves the request is not a cross-site ride-along.
        var cookieIdentity = default(System.Security.Claims.ClaimsIdentity);
        foreach (var identity in identities)
        {
            if (!identity.IsAuthenticated)
            {
                continue;
            }
            if (identity.AuthenticationType == FeatlyAuthenticationDefaults.CookieScheme)
            {
                cookieIdentity = identity;
            }
            else
            {
                return await next(context).ConfigureAwait(false);
            }
        }
        if (cookieIdentity is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var expected = cookieIdentity.FindFirst(FeatlyAuthenticationDefaults.CsrfClaim)?.Value;
        var presented = http.Request.Headers[FeatlyAuthenticationDefaults.CsrfHeader].ToString();
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented) || !FixedTimeEquals(expected, presented))
        {
            return Results.Problem(
                detail: $"This mutation requires the {FeatlyAuthenticationDefaults.CsrfHeader} header matching the session's anti-forgery token (returned by login and /api/auth/me). Sign in again if the session predates the token.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>Mints a new per-session anti-forgery token (256-bit, hex).</summary>
    public static string MintToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static bool FixedTimeEquals(string a, string b)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(a);
        var right = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
