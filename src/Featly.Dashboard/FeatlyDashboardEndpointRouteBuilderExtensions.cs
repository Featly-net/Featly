using System.Net.Mime;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Featly.Dashboard;

/// <summary>
/// Endpoint-routing extensions for mounting the embedded Featly dashboard.
/// </summary>
public static class FeatlyDashboardEndpointRouteBuilderExtensions
{
    private const string IndexResourceName = "Featly.Dashboard.wwwroot.index.html";

    /// <summary>
    /// Maps the embedded Featly dashboard at the given URL prefix.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="mountPath">URL prefix. Defaults to <c>/featly</c>.</param>
    public static IEndpointConventionBuilder MapFeatlyDashboard(
        this IEndpointRouteBuilder endpoints,
        string mountPath = "/featly")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(mountPath);

        var normalized = mountPath.StartsWith('/') ? mountPath : "/" + mountPath;

        return endpoints.MapGet(normalized, ServeIndexAsync)
            .WithName("Featly.Dashboard.Index")
            .WithDescription("Embedded Featly dashboard (placeholder until M5).");
    }

    private static async Task ServeIndexAsync(HttpContext context)
    {
        var assembly = typeof(FeatlyDashboardEndpointRouteBuilderExtensions).Assembly;
        await using var stream = assembly.GetManifestResourceStream(IndexResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{IndexResourceName}' is missing. Did the build drop wwwroot/index.html?");

        context.Response.ContentType = MediaTypeNames.Text.Html;
        context.Response.Headers.CacheControl = "no-store";
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
    }
}
