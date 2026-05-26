using System.Net.Mime;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Featly.Dashboard;

/// <summary>
/// Endpoint-routing extensions for mounting the embedded Featly dashboard.
/// </summary>
/// <remarks>
/// M5A ships a navigable SPA skeleton. Three assets are embedded in the
/// assembly (<c>index.html</c>, <c>app.css</c>, <c>app.js</c>); the middleware
/// serves them at the configured mount path plus a fallback that lets the
/// client-side router handle deep links like <c>/featly/flags</c>.
/// </remarks>
public static class FeatlyDashboardEndpointRouteBuilderExtensions
{
    private const string IndexResourceName = "Featly.Dashboard.wwwroot.index.html";
    private const string CssResourceName = "Featly.Dashboard.wwwroot.app.css";
    private const string JsResourceName = "Featly.Dashboard.wwwroot.app.js";
    private const string MountPlaceholder = "__MOUNT_PATH__";

    private static readonly Assembly s_assembly = typeof(FeatlyDashboardEndpointRouteBuilderExtensions).Assembly;

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

        var normalized = NormalizeMountPath(mountPath);
        var group = endpoints.MapGroup(normalized);

        // app.css / app.js — long-cacheable static assets.
        group.MapGet("/app.css", () => ServeAssetAsync(CssResourceName, "text/css"))
            .WithName("Featly.Dashboard.Css");
        group.MapGet("/app.js", () => ServeAssetAsync(JsResourceName, "text/javascript"))
            .WithName("Featly.Dashboard.Js");

        // index.html for the mount root and any sub-path. The SPA router on
        // the client takes it from there.
        var rootEndpoint = group.MapGet("/", ctx => ServeIndexAsync(ctx, normalized))
            .WithName("Featly.Dashboard.Index")
            .WithDescription("Embedded Featly dashboard (M5 skeleton).");
        group.MapGet("/{**path}", ctx => ServeIndexAsync(ctx, normalized));

        return rootEndpoint;
    }

    private static async Task ServeIndexAsync(HttpContext context, string mountPath)
    {
        var html = await ReadResourceAsTextAsync(IndexResourceName).ConfigureAwait(false);
        html = html.Replace(MountPlaceholder, mountPath, StringComparison.Ordinal);

        context.Response.ContentType = MediaTypeNames.Text.Html;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(html, Encoding.UTF8, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<IResult> ServeAssetAsync(string resourceName, string contentType)
    {
        var bytes = await ReadResourceBytesAsync(resourceName).ConfigureAwait(false);
        return Results.Bytes(bytes, contentType);
    }

    private static async Task<string> ReadResourceAsTextAsync(string resourceName)
    {
        await using var stream = OpenResourceOrThrow(resourceName);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadResourceBytesAsync(string resourceName)
    {
        await using var stream = OpenResourceOrThrow(resourceName);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static Stream OpenResourceOrThrow(string resourceName)
    {
        return s_assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' is missing. Did the build drop the file from wwwroot?");
    }

    private static string NormalizeMountPath(string mountPath)
    {
        var trimmed = mountPath.Trim();
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }
        if (trimmed.Length > 1 && trimmed.EndsWith('/'))
        {
            trimmed = trimmed[..^1];
        }
        return trimmed;
    }
}
