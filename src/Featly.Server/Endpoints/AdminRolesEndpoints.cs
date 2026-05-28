using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for managing <see cref="Role"/> entities. System roles
/// are read-only (the store rejects mutations); custom roles are created by
/// cloning a system template and editing the permission set, which protects
/// the meaning of well-known names like <c>viewer</c> (ARCHITECTURE.md §11).
/// </summary>
internal static class AdminRolesEndpoints
{
    public static RouteGroupBuilder MapAdminRoles(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/roles").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Roles.List").RequirePermission(Permission.RoleRead);
        admin.MapGet("/{key}", GetAsync).WithName("Featly.Admin.Roles.Get").RequirePermission(Permission.RoleRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Roles.Create").RequirePermission(Permission.RoleCreate);
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Roles.Update").RequirePermission(Permission.RoleUpdate);
        admin.MapDelete("/{key}", DeleteAsync).WithName("Featly.Admin.Roles.Delete").RequirePermission(Permission.RoleDelete);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, CancellationToken ct)
    {
        var roles = await store.Roles.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(roles);
    }

    private static async Task<IResult> GetAsync(string key, StorageFacade store, CancellationToken ct)
    {
        var role = await store.Roles.GetByKeyAsync(key, ct).ConfigureAwait(false);
        return role is null ? Results.NotFound(new { error = $"Role '{key}' not found." }) : Results.Ok(role);
    }

    private static async Task<IResult> CreateAsync(RoleWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(body.Key))
        {
            return Results.BadRequest(new { error = "key is required." });
        }
        if (SystemRoles.Template(body.Key) is not null)
        {
            return Results.Conflict(new { error = $"'{body.Key}' is a reserved system role key." });
        }

        var existing = await store.Roles.GetByKeyAsync(body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Role '{body.Key}' already exists." });
        }

        // Optional clone-of-system: seed the permission set from a system
        // template, then layer the request's explicit permissions on top.
        var permissions = ResolvePermissions(body);

        var now = DateTimeOffset.UtcNow;
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Key = body.Key,
            Name = string.IsNullOrWhiteSpace(body.Name) ? body.Key : body.Name,
            Description = body.Description,
            IsSystem = false,
            Permissions = permissions,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.Roles.UpsertAsync(role, ct).ConfigureAwait(false);
        return Results.Created($"/api/admin/roles/{role.Key}", role);
    }

    private static async Task<IResult> UpdateAsync(string key, RoleWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var existing = await store.Roles.GetByKeyAsync(key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Role '{key}' not found." });
        }
        if (existing.IsSystem)
        {
            return Results.Problem(detail: $"Role '{key}' is a system role and cannot be edited.", statusCode: StatusCodes.Status403Forbidden);
        }

        existing.Name = string.IsNullOrWhiteSpace(body.Name) ? existing.Name : body.Name;
        existing.Description = body.Description;
        existing.Permissions = [.. (body.Permissions ?? [])];

        await store.Roles.UpsertAsync(existing, ct).ConfigureAwait(false);
        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteAsync(string key, StorageFacade store, CancellationToken ct)
    {
        var existing = await store.Roles.GetByKeyAsync(key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NoContent();
        }
        if (existing.IsSystem)
        {
            return Results.Problem(detail: $"Role '{key}' is a system role and cannot be deleted.", statusCode: StatusCodes.Status403Forbidden);
        }

        await store.Roles.DeleteAsync(key, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static List<Permission> ResolvePermissions(RoleWriteRequest body)
    {
        var set = new HashSet<Permission>();
        if (!string.IsNullOrWhiteSpace(body.CloneFromSystemRole))
        {
            var template = SystemRoles.Template(body.CloneFromSystemRole);
            if (template is not null)
            {
                foreach (var p in template.Permissions)
                {
                    set.Add(p);
                }
            }
        }
        foreach (var p in body.Permissions ?? [])
        {
            set.Add(p);
        }
        return [.. set];
    }
}

/// <summary>
/// Inbound shape for POST / PUT on the admin roles endpoint. On create,
/// <see cref="CloneFromSystemRole"/> seeds the permission set from a system
/// template before the explicit <see cref="Permissions"/> are unioned on top.
/// </summary>
public sealed record RoleWriteRequest(
    string Key,
    string? Name,
    string? Description,
    IReadOnlyList<Permission>? Permissions = null,
    string? CloneFromSystemRole = null);
