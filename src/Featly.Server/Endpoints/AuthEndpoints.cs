using System.Security.Claims;
using Featly.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Dashboard session endpoints: a cookie-based login that accepts the same
/// API keys the Bearer flow understands, plus logout and a small
/// <c>/me</c> probe the dashboard uses to decide whether to show the login
/// screen.
/// </summary>
/// <remarks>
/// <para>
/// Why cookie + Bearer instead of replacing one with the other? The
/// dashboard wants <c>HttpOnly; SameSite=Strict</c> cookies (so an XSS in
/// the host can't read the token), but SDK clients want a stateless
/// Bearer header (no cookie jar in a background service). Same auth, two
/// surfaces.
/// </para>
/// <para>
/// The login endpoint accepts:
/// <list type="bullet">
///   <item>The legacy <c>AdminApiKey</c> from <c>Featly:Server:AdminApiKey</c> — mints an admin session for backwards compatibility.</item>
///   <item>The legacy <c>SdkApiKey</c> — refused; SDK keys aren't dashboard users.</item>
///   <item>A real <see cref="ApiKey"/> from the store (M6 PR 6B) — resolved by prefix, verified by Argon2, identifier becomes the key's <c>Name</c>.</item>
/// </list>
/// </para>
/// </remarks>
internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth");
        auth.MapPost("/login", LoginAsync).WithName("Featly.Auth.Login");
        // Cast to Delegate so ASP.NET treats these as route handlers that
        // write IResult to the response, not as raw RequestDelegates that
        // discard it (analyzer ASP0016).
        auth.MapPost("/logout", (Delegate)LogoutAsync).WithName("Featly.Auth.Logout");
        auth.MapGet("/me", (Delegate)MeAsync).WithName("Featly.Auth.Me");
        return group;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest body,
        HttpContext http,
        StorageFacade store,
        IOptions<FeatlyServerOptions> serverOptions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.ApiKey))
        {
            return Results.BadRequest(new { error = "apiKey is required." });
        }

        var identity = await ResolveAsync(body.ApiKey, store, serverOptions.Value, ct).ConfigureAwait(false);
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        // SignIn writes the auth cookie. AuthenticationProperties.IsPersistent keeps
        // it across browser restarts; AllowRefresh extends the lifetime on each hit.
        var principal = new ClaimsPrincipal(identity);
        await http.SignInAsync(
            FeatlyAuthenticationDefaults.CookieScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            }).ConfigureAwait(false);

        return Results.Ok(new MeResponse(
            Identifier: identity.Name ?? "",
            DisplayName: identity.FindFirst("featly:display")?.Value ?? identity.Name ?? "",
            CsrfToken: identity.FindFirst(FeatlyAuthenticationDefaults.CsrfClaim)?.Value ?? ""));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(FeatlyAuthenticationDefaults.CookieScheme).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http)
    {
        // /me is a probe the dashboard hits before showing UI to decide
        // whether a session exists. There's no default authentication scheme,
        // so AuthenticationMiddleware doesn't pre-populate http.User from the
        // cookie — authenticate explicitly against the cookie scheme.
        var result = await http.AuthenticateAsync(FeatlyAuthenticationDefaults.CookieScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(result.Principal.Identity.Name))
        {
            return Results.Unauthorized();
        }
        var name = result.Principal.Identity.Name;
        var display = result.Principal.FindFirst("featly:display")?.Value ?? name;
        var csrf = result.Principal.FindFirst(FeatlyAuthenticationDefaults.CsrfClaim)?.Value ?? "";
        return Results.Ok(new MeResponse(name, display, csrf));
    }

    private static async Task<ClaimsIdentity?> ResolveAsync(
        string apiKey,
        StorageFacade store,
        FeatlyServerOptions options,
        CancellationToken ct)
    {
        // 1. Legacy admin key match → admin session. Constant-time compare to
        //    avoid timing oracles on the appsettings token.
        if (!string.IsNullOrEmpty(options.AdminApiKey) &&
            FixedTimeEquals(apiKey, options.AdminApiKey))
        {
            return BuildIdentity(name: "api-key:AdminWrite", display: "Admin (legacy key)", scheme: FeatlyAuthenticationDefaults.CookieScheme);
        }

        // 2. New ApiKey store: prefix lookup + Argon2 verify.
        var prefix = ApiKeyHasher.ExtractPrefix(apiKey);
        var candidates = await store.ApiKeys.FindCandidatesByPrefixAsync(prefix, ct).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            if (candidate.Scope != ApiKeyScope.AdminWrite)
            {
                continue;
            }
            if (ApiKeyHasher.Verify(apiKey, candidate.Hash))
            {
                // Expiry is enforced here exactly like the Bearer handler does:
                // an expired key must not open a (7-day sliding) cookie session.
                if (candidate.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
                {
                    return null;
                }

                // Touch lastUsed best-effort.
                _ = store.ApiKeys.TouchLastUsedAsync(candidate.Id, DateTimeOffset.UtcNow, CancellationToken.None);
                return BuildIdentity(name: candidate.Name, display: candidate.Name, scheme: FeatlyAuthenticationDefaults.CookieScheme);
            }
        }

        // 3. Legacy SDK key intentionally NOT accepted for the dashboard.
        return null;
    }

    private static ClaimsIdentity BuildIdentity(string name, string display, string scheme)
    {
        var identity = new ClaimsIdentity(scheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, name));
        identity.AddClaim(new Claim("featly:display", display));
        // Per-session anti-forgery token: lives inside the HttpOnly cookie as
        // a claim and is returned by login//me so the dashboard can echo it in
        // the X-Featly-Csrf header on every mutation (FeatlyCsrfFilter).
        identity.AddClaim(new Claim(FeatlyAuthenticationDefaults.CsrfClaim, FeatlyCsrfFilter.MintToken()));
        return identity;
    }

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

/// <summary>Inbound shape for <c>POST /api/auth/login</c>.</summary>
public sealed record LoginRequest(string ApiKey);

/// <summary>
/// Outbound shape for <c>POST /api/auth/login</c> and <c>GET /api/auth/me</c>.
/// <paramref name="CsrfToken"/> must be echoed in the <c>X-Featly-Csrf</c>
/// header on every cookie-authenticated mutation.
/// </summary>
public sealed record MeResponse(string Identifier, string DisplayName, string CsrfToken = "");
