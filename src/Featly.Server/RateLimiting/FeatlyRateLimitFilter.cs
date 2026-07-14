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
        if (!settings.Enabled && !IsAlwaysThrottled(surface, http.Request.Method))
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

    /// <summary>
    /// Credential-submitting auth POSTs (login) are always throttled as a
    /// brute-force / Argon2-DoS guard, even when the master switch is off. Read
    /// probes and the admin/SDK surfaces stay opt-in (issue #190).
    /// </summary>
    private static bool IsAlwaysThrottled(FeatlyRateSurface surface, string method)
        => surface == FeatlyRateSurface.Auth && HttpMethods.IsPost(method);

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
