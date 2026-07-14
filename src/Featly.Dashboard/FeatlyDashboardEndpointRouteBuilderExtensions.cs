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
/// <para>
/// All assets under <c>wwwroot/</c> are embedded into the assembly at build
/// time. Two route patterns are exposed under the configured mount path:
/// </para>
/// <list type="bullet">
///   <item><c>{file}.{ext}</c> where <c>ext</c> is <c>css</c>, <c>js</c>,
///         <c>svg</c>, <c>png</c>, <c>ico</c> or <c>webp</c> — served from
///         the embedded manifest with the correct <c>Content-Type</c>.</item>
///   <item>Anything else — the <c>index.html</c> shell, so deep links
///         like <c>/featly/flags/demo</c> survive a refresh and the
///         client-side router can mount the right view.</item>
/// </list>
/// </remarks>
public static class FeatlyDashboardEndpointRouteBuilderExtensions
{
    private const string IndexResourceName = "Featly.Dashboard.wwwroot.index.html";
    private const string ResourcePrefix = "Featly.Dashboard.wwwroot.";
    private const string MountPlaceholder = "__MOUNT_PATH__";

    private static readonly Assembly s_assembly = typeof(FeatlyDashboardEndpointRouteBuilderExtensions).Assembly;

    private static readonly Dictionary<string, string> s_contentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css",
        [".js"] = "text/javascript",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".webp"] = "image/webp",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
    };

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

        var rootEndpoint = group.MapGet("/", ctx => ServeIndexAsync(ctx, normalized))
            .WithName("Featly.Dashboard.Index")
            .WithDescription("Embedded Featly dashboard (M5 skeleton).");

        // Catch-all: assets (by extension) go to the embedded manifest, anything
        // else falls back to the shell so the client-side router can resolve it.
        group.MapGet("/{**path}", ctx => ServeAsync(ctx, normalized));

        return rootEndpoint;
    }

    private static Task ServeAsync(HttpContext context, string mountPath)
    {
        var path = (context.Request.RouteValues["path"] as string) ?? "";
        var ext = Path.GetExtension(path);
        if (ext.Length > 0 && s_contentTypes.TryGetValue(ext, out var contentType))
        {
            return ServeAssetAsync(context, path, contentType);
        }
        return ServeIndexAsync(context, mountPath);
    }

    private static async Task ServeAssetAsync(HttpContext context, string relativePath, string contentType)
    {
        var resourceName = ResourcePrefix + relativePath.Replace('/', '.');
        var stream = s_assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using (stream)
        {
            ApplySecurityHeaders(context.Response);
            context.Response.ContentType = contentType;
            context.Response.Headers.CacheControl = "no-cache";
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static async Task ServeIndexAsync(HttpContext context, string mountPath)
    {
        var html = await ReadResourceAsTextAsync(IndexResourceName).ConfigureAwait(false);
        html = html.Replace(MountPlaceholder, mountPath, StringComparison.Ordinal);

        ApplySecurityHeaders(context.Response);
        context.Response.ContentType = MediaTypeNames.Text.Html;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(html, Encoding.UTF8, context.RequestAborted).ConfigureAwait(false);
    }

    // The dashboard bundle is fully self-contained and same-origin (its CSS, JS,
    // and fonts are served from this mount), so a strict CSP is safe. Scripts are
    // external files only (no inline <script>), so script-src stays 'self'; the
    // bundle does set inline style="" attributes via rendered markup, so
    // style-src allows 'unsafe-inline' (styles cannot execute script). This is
    // defense in depth on top of the HttpOnly + SameSite=Strict session cookie.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "img-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self'; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "form-action 'self'";

    private static void ApplySecurityHeaders(HttpResponse response)
    {
        var headers = response.Headers;
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
    }

    private static async Task<string> ReadResourceAsTextAsync(string resourceName)
    {
        await using var stream = s_assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' is missing. Did the build drop the file from wwwroot?");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
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
