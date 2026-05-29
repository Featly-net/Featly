using System.Security.Claims;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for browsing <see cref="Environment"/> entities and
/// toggling the ReadOnly freeze (M10). The dashboard consumes the list endpoint
/// to populate its environment selector; lock/unlock drives the freeze switch.
/// </summary>
internal static class AdminEnvironmentsEndpoints
{
    public static RouteGroupBuilder MapAdminEnvironments(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/environments").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Environments.List").RequirePermission(Permission.EnvironmentRead);
        admin.MapPost("/{key}/lock", LockAsync).WithName("Featly.Admin.Environments.Lock").RequirePermission(Permission.EnvironmentLock);
        admin.MapPost("/{key}/unlock", UnlockAsync).WithName("Featly.Admin.Environments.Unlock").RequirePermission(Permission.EnvironmentLock);

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

    private static Task<IResult> LockAsync(string key, StorageFacade store, IFeatlyEventPublisher events, ClaimsPrincipal user, CancellationToken ct)
        => SetReadOnlyAsync(key, readOnly: true, store, events, user, ct);

    private static Task<IResult> UnlockAsync(string key, StorageFacade store, IFeatlyEventPublisher events, ClaimsPrincipal user, CancellationToken ct)
        => SetReadOnlyAsync(key, readOnly: false, store, events, user, ct);

    private static async Task<IResult> SetReadOnlyAsync(
        string key,
        bool readOnly,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Results.NotFound(new { error = "No default project." });
        }

        var environment = await store.Environments.GetByKeyAsync(project.Id, key, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{key}' not found." });
        }

        var updated = await store.Environments.SetReadOnlyAsync(environment.Id, readOnly, ct).ConfigureAwait(false);
        await events.PublishAsync(
            readOnly ? FeatlyEventTypes.EnvironmentLocked : FeatlyEventTypes.EnvironmentUnlocked,
            "Environment", environment.Key, environment.Id, user, new { environment.Key, readOnly }, ct).ConfigureAwait(false);

        return Results.Ok(updated);
    }
}
