using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for browsing <see cref="Environment"/> entities.
/// Read-only in M5 — environment creation lands alongside the dashboard
/// settings screens in a later milestone. The dashboard consumes the
/// list endpoint to populate its environment selector.
/// </summary>
internal static class AdminEnvironmentsEndpoints
{
    public static RouteGroupBuilder MapAdminEnvironments(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/environments").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Environments.List").RequirePermission(Permission.EnvironmentRead);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Results.Ok(Array.Empty<object>());
        }

        var environments = await store.Environments.ListAsync(project.Id, ct).ConfigureAwait(false);
        return Results.Ok(environments);
    }
}
