using Featly.Server.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Featly.Server;

/// <summary>
/// Endpoint-routing extensions for mounting Featly's HTTP surface inside
/// any ASP.NET Core application.
/// </summary>
public static class FeatlyServerEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the Featly HTTP API and health endpoints. M2 exposes:
    /// <list type="bullet">
    ///   <item><c>GET /health/live</c> — liveness probe (no auth).</item>
    ///   <item><c>GET|POST|PUT /api/admin/flags/...</c> — admin CRUD (admin token).</item>
    ///   <item><c>GET|POST|PUT|DELETE /api/admin/segments/...</c> — admin CRUD for reusable segments (admin token).</item>
    ///   <item><c>GET|POST|PUT /api/admin/configs/...</c> — admin CRUD for dynamic configurations (admin token).</item>
    ///   <item><c>GET /api/admin/environments</c> — list environments (admin token).</item>
    ///   <item><c>POST /api/admin/preview/flags/{key}</c> and <c>/configs/{key}</c> — server-side dry-run evaluation against a candidate context (admin token).</item>
    ///   <item><c>GET /api/sdk/config</c> — config snapshot, flags + segments + configs (SDK token).</item>
    ///   <item><c>GET /api/sdk/stream</c> — SSE change notifications (SDK token).</item>
    /// </list>
    /// </summary>
    public static RouteGroupBuilder MapFeatlyApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var features = endpoints.ServiceProvider.GetRequiredService<IOptions<FeatlyServerOptions>>().Value.Features;

        var group = endpoints.MapGroup("/").WithTags("Featly");

        group.MapGet("/health/live", () => Results.Ok(new HealthResponse("live")))
             .WithName("Featly.Health.Live")
             .WithDescription("Liveness probe. Returns 200 if the host process can respond.");

        var apiGroup = group.MapGroup("/api");

        // Version negotiation (issue #227): honour the client's Accept-Version
        // pin, echo the served version, announce sunsets. First in the chain so
        // an unsupported pin is refused before any work happens.
        apiGroup.AddEndpointFilter(new FeatlyApiVersionFilter());

        // Request throttling (opt-in via Featly:RateLimiting / the settings API).
        // One filter at the /api root covers every Featly endpoint with no host
        // pipeline change; disabled it forwards straight through.
        apiGroup.AddEndpointFilter(new RateLimiting.FeatlyRateLimitFilter());

        // Synchronizer-token CSRF layer: cookie-authenticated mutations must
        // echo the session token in X-Featly-Csrf. Bearer and anonymous
        // requests pass through (see FeatlyCsrfFilter).
        apiGroup.AddEndpointFilter(new Authentication.FeatlyCsrfFilter());

        // Always-on core: the rest of the product depends on these.
        apiGroup.MapAuth();
        apiGroup.MapBootstrap();
        apiGroup.MapMeta(features);
        apiGroup.MapAdminEnvironments();
        apiGroup.MapAdminProjects();
        apiGroup.MapAdminApiKeys();
        apiGroup.MapAdminSettings();
        apiGroup.MapAdminExport();
        apiGroup.MapSdkEndpoints();
        apiGroup.MapSdkEvents();

        // Opt-out feature areas (ADR-0024). Default: all on.
        if (features.Flags)
        {
            apiGroup.MapAdminFlags();
        }
        if (features.Configs)
        {
            apiGroup.MapAdminConfigs();
        }
        if (features.Segments)
        {
            apiGroup.MapAdminSegments();
        }
        if (features.Flags || features.Configs)
        {
            // "Test this context" preview covers flags and configs.
            apiGroup.MapAdminPreview();
        }
        if (features.Experiments)
        {
            apiGroup.MapAdminExperiments();
        }
        if (features.Approvals)
        {
            apiGroup.MapAdminChanges();
            apiGroup.MapAdminApprovalPolicies();
        }
        if (features.Webhooks)
        {
            apiGroup.MapAdminWebhooks();
        }
        if (features.Audit)
        {
            apiGroup.MapAdminAudit();
        }
        if (features.Rbac)
        {
            apiGroup.MapAdminUsers();
            apiGroup.MapAdminRoles();
            apiGroup.MapAdminGroups();
            apiGroup.MapAdminRoleAssignments();
            apiGroup.MapAdminRoleUpgradeRequests();
        }

        return group;
    }

    private sealed record HealthResponse(string Status);
}
