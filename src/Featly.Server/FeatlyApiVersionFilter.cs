using System.Globalization;
using Featly.Server.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Featly.Server;

/// <summary>
/// Negotiates the API version for every <c>/api</c> request (issue #227): honours
/// the client's <c>Accept-Version</c> pin, echoes the served version back, and
/// announces a sunset date for versions that are on their way out. Attached once
/// at the <c>/api</c> group root, like the CSRF and rate-limit filters.
/// </summary>
/// <param name="deprecated">
/// Supported-but-deprecated majors mapped to their sunset date. Defaults to
/// <see cref="FeatlyApiVersion.Deprecated"/>; injectable so the announcement path
/// is testable while no real version is deprecated yet.
/// </param>
internal sealed class FeatlyApiVersionFilter(IReadOnlyDictionary<string, DateTimeOffset>? deprecated = null) : IEndpointFilter
{
    private readonly IReadOnlyDictionary<string, DateTimeOffset> _deprecated = deprecated ?? FeatlyApiVersion.Deprecated;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;

        var pinned = http.Request.Headers[FeatlyApiVersion.RequestHeader].ToString();
        var served = FeatlyApiVersion.Current;

        if (!string.IsNullOrWhiteSpace(pinned))
        {
            var major = FeatlyApiVersion.Major(pinned);
            if (major is null || !FeatlyApiVersion.Supported.Contains(major, StringComparer.Ordinal))
            {
                // Refuse rather than serve a shape the client cannot parse.
                return Problems.NotAcceptable(
                    $"Unsupported API version '{pinned}'. This server serves: {string.Join(", ", FeatlyApiVersion.Supported)}.");
            }

            served = major;
        }

        http.Response.Headers[FeatlyApiVersion.ResponseHeader] = served;
        if (_deprecated.TryGetValue(served, out var sunset))
        {
            http.Response.Headers["Deprecation"] = "true";
            http.Response.Headers["Sunset"] = sunset.ToString("R", CultureInfo.InvariantCulture);
        }

        return await next(context).ConfigureAwait(false);
    }
}
