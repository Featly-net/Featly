using Featly.Server.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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

        var group = endpoints.MapGroup("/").WithTags("Featly");

        group.MapGet("/health/live", () => Results.Ok(new HealthResponse("live")))
             .WithName("Featly.Health.Live")
             .WithDescription("Liveness probe. Returns 200 if the host process can respond.");

        var apiGroup = group.MapGroup("/api");
        apiGroup.MapAuth();
        apiGroup.MapAdminFlags();
        apiGroup.MapAdminSegments();
        apiGroup.MapAdminConfigs();
        apiGroup.MapAdminEnvironments();
        apiGroup.MapAdminPreview();
        apiGroup.MapAdminUsers();
        apiGroup.MapAdminRoles();
        apiGroup.MapAdminGroups();
        apiGroup.MapAdminRoleAssignments();
        apiGroup.MapAdminRoleUpgradeRequests();
        apiGroup.MapAdminChanges();
        apiGroup.MapAdminApprovalPolicies();
        apiGroup.MapAdminExperiments();
        apiGroup.MapAdminAudit();
        apiGroup.MapAdminWebhooks();
        apiGroup.MapSdkEndpoints();
        apiGroup.MapSdkEvents();

        return group;
    }

    private sealed record HealthResponse(string Status);
}
