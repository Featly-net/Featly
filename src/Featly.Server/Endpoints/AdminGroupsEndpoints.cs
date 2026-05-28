using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for managing <see cref="UserGroup"/> entities. A group
/// bundles users so a single <see cref="RoleAssignment"/> can grant a role to
/// all members at once.
/// </summary>
internal static class AdminGroupsEndpoints
{
    public static RouteGroupBuilder MapAdminGroups(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/groups").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Groups.List").RequirePermission(Permission.GroupRead);
        admin.MapGet("/{key}", GetAsync).WithName("Featly.Admin.Groups.Get").RequirePermission(Permission.GroupRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Groups.Create").RequirePermission(Permission.GroupCreate);
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Groups.Update").RequirePermission(Permission.GroupUpdate);
        admin.MapDelete("/{key}", DeleteAsync).WithName("Featly.Admin.Groups.Delete").RequirePermission(Permission.GroupDelete);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, CancellationToken ct)
    {
        var groups = await store.Groups.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(groups);
    }

    private static async Task<IResult> GetAsync(string key, StorageFacade store, CancellationToken ct)
    {
        var group = await store.Groups.GetByKeyAsync(key, ct).ConfigureAwait(false);
        return group is null ? Results.NotFound(new { error = $"Group '{key}' not found." }) : Results.Ok(group);
    }

    private static async Task<IResult> CreateAsync(GroupWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Key))
        {
            return Results.BadRequest(new { error = "key is required." });
        }

        var existing = await store.Groups.GetByKeyAsync(body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Group '{body.Key}' already exists." });
        }

        var now = DateTimeOffset.UtcNow;
        var group = new UserGroup
        {
            Id = Guid.NewGuid(),
            Key = body.Key,
            Name = string.IsNullOrWhiteSpace(body.Name) ? body.Key : body.Name,
            Description = body.Description,
            MemberUserIds = [.. (body.MemberUserIds ?? [])],
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.Groups.UpsertAsync(group, ct).ConfigureAwait(false);
        return Results.Created($"/api/admin/groups/{group.Key}", group);
    }

    private static async Task<IResult> UpdateAsync(string key, GroupWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var existing = await store.Groups.GetByKeyAsync(key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Group '{key}' not found." });
        }

        existing.Name = string.IsNullOrWhiteSpace(body.Name) ? existing.Name : body.Name;
        existing.Description = body.Description;
        existing.MemberUserIds = [.. (body.MemberUserIds ?? [])];

        await store.Groups.UpsertAsync(existing, ct).ConfigureAwait(false);
        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteAsync(string key, StorageFacade store, CancellationToken ct)
    {
        await store.Groups.DeleteAsync(key, ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}

/// <summary>Inbound shape for POST / PUT on the admin groups endpoint.</summary>
public sealed record GroupWriteRequest(
    string Key,
    string? Name,
    string? Description,
    IReadOnlyList<Guid>? MemberUserIds = null);
