namespace Featly.Server.Authentication;

/// <summary>Authentication scheme names and constants used by Featly server.</summary>
public static class FeatlyAuthenticationDefaults
{
    /// <summary>Scheme name for the admin API bearer token.</summary>
    public const string AdminScheme = "FeatlyAdmin";

    /// <summary>Scheme name for the SDK API bearer token.</summary>
    public const string SdkScheme = "FeatlySdk";

    /// <summary>
    /// Scheme name for the dashboard cookie session. M6 PR 6D added the
    /// <c>POST /api/auth/login</c> endpoint that mints this cookie. Listed
    /// alongside <see cref="AdminScheme"/> in the admin policy so the same
    /// admin endpoints accept either Bearer (SDK / scripts) or cookie
    /// (browser).
    /// </summary>
    public const string CookieScheme = "FeatlyCookie";

    /// <summary>Cookie name written by the dashboard session.</summary>
    public const string CookieName = "featly.session";

    /// <summary>Claim type identifying which API key scope authenticated the request.</summary>
    public const string ScopeClaim = "featly:scope";

    /// <summary>
    /// Claim type carrying the environment id a persisted <see cref="ApiKey"/> is
    /// bound to. Present only for persisted keys; the static bootstrap key carries
    /// no binding and is treated as wildcard. SDK endpoints reject a request whose
    /// resolved environment differs from this claim (ADR-0009 scope enforcement).
    /// </summary>
    public const string EnvironmentClaim = "featly:env";

    /// <summary>
    /// Claim type carrying the per-session anti-forgery token inside the
    /// dashboard cookie. Mutating requests authenticated by the cookie must
    /// echo it in the <see cref="CsrfHeader"/> header (synchronizer token).
    /// </summary>
    public const string CsrfClaim = "featly:csrf";

    /// <summary>Request header that must echo the session's <see cref="CsrfClaim"/> on cookie-authenticated mutations.</summary>
    public const string CsrfHeader = "X-Featly-Csrf";

    /// <summary>Authorization policy name protecting <c>/api/admin/*</c> endpoints.</summary>
    public const string AdminPolicy = "Featly.Admin";

    /// <summary>Authorization policy name protecting <c>/api/sdk/*</c> endpoints.</summary>
    public const string SdkPolicy = "Featly.Sdk";
}
