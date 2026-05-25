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
    /// Maps the Featly HTTP API and health endpoints. M1 only exposes
    /// <c>GET /health/live</c>; the SDK and Admin APIs come online in M2.
    /// </summary>
    /// <returns>A group convention builder for further customization.</returns>
    public static RouteGroupBuilder MapFeatlyApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/").WithTags("Featly");

        group.MapGet("/health/live", () => Results.Ok(new HealthResponse("live")))
             .WithName("Featly.Health.Live")
             .WithDescription("Liveness probe. Returns 200 if the host process can respond.");

        // M2: GET /api/sdk/config, GET /api/sdk/stream, POST /api/sdk/events
        // M2: POST /api/admin/flags, PUT /api/admin/flags/{key}
        // M6: bearer auth on /api/admin/*

        return group;
    }

    private sealed record HealthResponse(string Status);
}
