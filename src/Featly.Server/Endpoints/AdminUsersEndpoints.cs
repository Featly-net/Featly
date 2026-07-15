using System.Security.Claims;
using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for managing <see cref="User"/> entities. Users are
/// usually auto-provisioned by the auth pipeline on first sight (Open mode),
/// but an admin can pre-create them (Closed mode onboarding) and disable
/// accounts here.
/// </summary>
internal static class AdminUsersEndpoints
{
    public static RouteGroupBuilder MapAdminUsers(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/users").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Users.List").RequirePermission(Permission.UserRead);
        admin.MapGet("/{identifier}", GetAsync).WithName("Featly.Admin.Users.Get").RequirePermission(Permission.UserRead);
        admin.MapGet("/{identifier}/effective-access", EffectiveAccessAsync).WithName("Featly.Admin.Users.EffectiveAccess").RequirePermission(Permission.UserRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Users.Create").RequirePermission(Permission.UserCreate);
        admin.MapPost("/{identifier}/disable", DisableAsync).WithName("Featly.Admin.Users.Disable").RequirePermission(Permission.UserDisable);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, CancellationToken ct)
    {
        var users = await store.Users.ListAsync(ct).ConfigureAwait(false);
        return Results.Ok(users);
    }

    private static async Task<IResult> GetAsync(string identifier, StorageFacade store, CancellationToken ct)
    {
        var user = await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
        return user is null ? Problems.NotFound($"User '{identifier}' not found.") : Results.Ok(user);
    }

    // GET /{identifier}/effective-access?projectId=&environmentId=
    // Returns the union of permissions the user holds in the given scope and
    // which roles / assignments contributed them — the "why does this user have
    // this access" view (ARCHITECTURE.md §11).
    private static async Task<IResult> EffectiveAccessAsync(
        string identifier,
        StorageFacade store,
        Guid? projectId,
        Guid? environmentId,
        CancellationToken ct)
    {
        var user = await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Problems.NotFound($"User '{identifier}' not found.");
        }

        // Resolve the scope project: default to the bootstrap project.
        var scopeProjectId = projectId ?? Guid.Empty;
        if (scopeProjectId == Guid.Empty)
        {
            var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
            scopeProjectId = project?.Id ?? Guid.Empty;
        }

        // Expand the user into assignee ids: itself + every group it belongs to.
        var assigneeIds = new List<Guid> { user.Id };
        var groups = await store.Groups.ListForMemberAsync(user.Id, ct).ConfigureAwait(false);
        foreach (var g in groups)
        {
            assigneeIds.Add(g.Id);
        }

        var assignments = await store.RoleAssignments.ListForAssigneesAsync(assigneeIds, ct).ConfigureAwait(false);

        var permissions = new HashSet<Permission>();
        var contributingRoles = new List<EffectiveRole>();
        var seenRoles = new HashSet<Guid>();
        foreach (var a in assignments)
        {
            if (a.ProjectId != scopeProjectId)
            {
                continue;
            }
            if (a.EnvironmentId is not null && a.EnvironmentId != environmentId)
            {
                continue;
            }
            if (!seenRoles.Add(a.RoleId))
            {
                continue;
            }
            var role = await store.Roles.GetByIdAsync(a.RoleId, ct).ConfigureAwait(false);
            if (role is null)
            {
                continue;
            }
            foreach (var p in role.Permissions)
            {
                permissions.Add(p);
            }
            contributingRoles.Add(new EffectiveRole(role.Key, role.Name, a.AssigneeType, a.EnvironmentId));
        }

        var response = new EffectiveAccessResponse(
            Identifier: user.Identifier,
            ProjectId: scopeProjectId,
            EnvironmentId: environmentId,
            Roles: contributingRoles,
            Permissions: [.. permissions.OrderBy(p => p.ToString(), StringComparer.Ordinal)]);
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateAsync(UserWriteRequest body, StorageFacade store, ClaimsPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Identifier))
        {
            return Problems.Validation("identifier", "identifier is required.");
        }

        var actor = AdminWrite.ResolveActor(principal);
        var existing = await store.Users.GetByIdentifierAsync(body.Identifier, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var user = new User
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Identifier = body.Identifier,
            DisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? body.Identifier : body.DisplayName,
            Email = body.Email ?? (body.Identifier.Contains('@', StringComparison.Ordinal) ? body.Identifier : null),
            Disabled = body.Disabled ?? existing?.Disabled ?? false,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            CreatedBy = existing?.CreatedBy ?? actor,
            UpdatedBy = actor,
        };
        await store.Users.UpsertAsync(user, actor, ct).ConfigureAwait(false);

        return existing is null
            ? Results.Created($"/api/admin/users/{user.Identifier}", user)
            : Results.Ok(user);
    }

    private static async Task<IResult> DisableAsync(string identifier, StorageFacade store, ClaimsPrincipal principal, CancellationToken ct)
    {
        var existing = await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Problems.NotFound($"User '{identifier}' not found.");
        }

        await store.Users.DisableAsync(identifier, AdminWrite.ResolveActor(principal), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

}

/// <summary>Inbound shape for POST on the admin users endpoint (create / upsert).</summary>
public sealed record UserWriteRequest(
    string Identifier,
    string? DisplayName = null,
    string? Email = null,
    bool? Disabled = null);

/// <summary>Outbound shape for the effective-access view: which permissions a user holds in a scope and why.</summary>
public sealed record EffectiveAccessResponse(
    string Identifier,
    Guid ProjectId,
    Guid? EnvironmentId,
    IReadOnlyList<EffectiveRole> Roles,
    IReadOnlyList<Permission> Permissions);

/// <summary>One role that contributed to a user's effective access, with how it was granted.</summary>
public sealed record EffectiveRole(
    string Key,
    string Name,
    AssigneeType Via,
    Guid? EnvironmentId);
