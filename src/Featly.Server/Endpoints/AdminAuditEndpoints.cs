using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API for querying the audit log (ARCHITECTURE.md §17). Read-only, behind
/// the admin policy with the <see cref="Permission.AuditRead"/> permission.
/// </summary>
internal static class AdminAuditEndpoints
{
    public static RouteGroupBuilder MapAdminAudit(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/audit").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", QueryAsync).WithName("Featly.Admin.Audit.Query").RequirePermission(Permission.AuditRead);

        return group;
    }

    private static async Task<IResult> QueryAsync(
        StorageFacade store,
        CancellationToken ct,
        string? entityType = null,
        string? entityKey = null,
        string? actor = null,
        string? env = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 200)
    {
        // Optional environment filter: callers pass the env key, resolved to its id.
        Guid? environmentId = null;
        if (!string.IsNullOrWhiteSpace(env))
        {
            var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
            if (environment is null)
            {
                return Results.NotFound(new { error = $"Environment '{env}' not found." });
            }

            environmentId = environment.Id;
        }

        var entries = await store.Audit
            .QueryAsync(entityType, entityKey, actor, environmentId, from, to, Math.Clamp(limit, 1, 1000), ct)
            .ConfigureAwait(false);
        return Results.Ok(entries);
    }

    private static async Task<Environment?> ResolveEnvironmentAsync(StorageFacade store, string envKey, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        return await store.Environments.GetByKeyAsync(project.Id, envKey, ct).ConfigureAwait(false);
    }
}
