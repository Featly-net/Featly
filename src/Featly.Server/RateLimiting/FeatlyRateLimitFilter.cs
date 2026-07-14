using Featly.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Featly.Server.RateLimiting;

/// <summary>
/// Endpoint filter that throttles the Featly HTTP surface. Attached once at the
/// <c>/api</c> group so it covers every Featly endpoint without any change to
/// the host pipeline (the embedded quickstart stays "two DI calls + a mount").
/// The surface is derived from the request path; the client partition is the
/// authenticated identity when present, else the remote IP. Disabled (the
/// default) it forwards straight to the endpoint.
/// </summary>
/// <remarks>
/// Rejections return <c>429 Too Many Requests</c> with a <c>Retry-After</c> of
/// the window size. The limiter runs after authentication (endpoint filters run
/// inside routing), so a valid identity is throttled per identity — one noisy
/// API key cannot starve the whole host IP behind a NAT, and an attacker cannot
/// spoof someone else's bucket without their credentials.
/// </remarks>
internal sealed class FeatlyRateLimitFilter : IEndpointFilter
{
    private const int WindowSeconds = 60;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var settings = http.RequestServices.GetRequiredService<IFeatlySettingsProvider>().RateLimit;

        var surface = SurfaceOf(http.Request.Path);

        // The auth surface is throttled even when the master switch is off: a
        // credential-submitting POST to /api/auth/* (login) is an unauthenticated
        // brute-force / Argon2-DoS vector, so it always carries a per-client
        // limit. Read probes (GET /me) and the opt-in admin/SDK surfaces still
        // honor the master switch (issue #190).
        var alwaysOnAuth = surface == FeatlyRateSurface.Auth && HttpMethods.IsPost(http.Request.Method);
        if (!settings.Enabled && !alwaysOnAuth)
        {
            return await next(context).ConfigureAwait(false);
        }

        var limit = surface switch
        {
            FeatlyRateSurface.Auth => settings.AuthPermitsPerMinute,
            FeatlyRateSurface.Sdk => settings.SdkPermitsPerMinute,
            _ => settings.AdminPermitsPerMinute,
        };

        var limiter = http.RequestServices.GetRequiredService<FeatlyRateLimiter>();
        using var lease = await limiter.AcquireAsync(surface, ClientOf(http), limit, http.RequestAborted).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            http.Response.Headers.RetryAfter = WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static FeatlyRateSurface SurfaceOf(PathString path)
    {
        if (path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            return FeatlyRateSurface.Auth;
        }
        if (path.StartsWithSegments("/api/sdk", StringComparison.OrdinalIgnoreCase))
        {
            return FeatlyRateSurface.Sdk;
        }
        return FeatlyRateSurface.Admin;
    }

    private static string ClientOf(HttpContext http)
    {
        var name = http.User?.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null;
        if (!string.IsNullOrEmpty(name))
        {
            return "id:" + name;
        }
        return "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
