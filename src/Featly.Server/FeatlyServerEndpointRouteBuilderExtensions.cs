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
    ///   <item><c>GET /api/sdk/config</c> — config snapshot (SDK token).</item>
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
        apiGroup.MapAdminFlags();
        apiGroup.MapSdkEndpoints();

        return group;
    }

    private sealed record HealthResponse(string Status);
}
