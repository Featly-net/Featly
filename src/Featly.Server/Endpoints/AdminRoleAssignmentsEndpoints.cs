using System.Security.Claims;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for managing <see cref="RoleAssignment"/> rows — the
/// polymorphic grant of a role to a user or group, scoped to a project and
/// optionally an environment. Creating and deleting assignments is gated on
/// <see cref="Permission.UserUpdateRole"/>; listing on <see cref="Permission.UserRead"/>.
/// </summary>
internal static class AdminRoleAssignmentsEndpoints
{
    public static RouteGroupBuilder MapAdminRoleAssignments(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/role-assignments").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.RoleAssignments.List").RequirePermission(Permission.UserRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.RoleAssignments.Create").RequirePermission(Permission.UserUpdateRole);
        admin.MapDelete("/{id:guid}", DeleteAsync).WithName("Featly.Admin.RoleAssignments.Delete").RequirePermission(Permission.UserUpdateRole);

        return group;
    }

    // GET /?projectId=...  or  /?assigneeId=...  (one is required)
    private static async Task<IResult> ListAsync(StorageFacade store, Guid? projectId, Guid? assigneeId, CancellationToken ct)
    {
        if (assigneeId is { } aid)
        {
            return Results.Ok(await store.RoleAssignments.ListForAssigneeAsync(aid, ct).ConfigureAwait(false));
        }
        if (projectId is { } pid)
        {
            return Results.Ok(await store.RoleAssignments.ListForProjectAsync(pid, ct).ConfigureAwait(false));
        }

        // Default to the current default project so the dashboard's single-project
        // view works without passing an id.
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Results.Ok(Array.Empty<RoleAssignment>());
        }
        return Results.Ok(await store.RoleAssignments.ListForProjectAsync(project.Id, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> CreateAsync(
        RoleAssignmentWriteRequest body,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Validate the referenced role exists.
        var role = await store.Roles.GetByIdAsync(body.RoleId, ct).ConfigureAwait(false);
        if (role is null)
        {
            return Results.BadRequest(new { error = $"Role '{body.RoleId}' not found." });
        }

        // Validate the assignee exists (user or group).
        if (body.AssigneeType == AssigneeType.User)
        {
            if (await store.Users.GetByIdAsync(body.AssigneeId, ct).ConfigureAwait(false) is null)
            {
                return Results.BadRequest(new { error = $"User '{body.AssigneeId}' not found." });
            }
        }
        else
        {
            if (await store.Groups.GetByIdAsync(body.AssigneeId, ct).ConfigureAwait(false) is null)
            {
                return Results.BadRequest(new { error = $"Group '{body.AssigneeId}' not found." });
            }
        }

        // Resolve the project: default to the current default project.
        var projectId = body.ProjectId;
        if (projectId == Guid.Empty)
        {
            var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
            if (project is null)
            {
                return Results.BadRequest(new { error = "No default project to scope the assignment to." });
            }
            projectId = project.Id;
        }

        var assignment = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = body.AssigneeType,
            AssigneeId = body.AssigneeId,
            ProjectId = projectId,
            EnvironmentId = body.EnvironmentId,
            RoleId = body.RoleId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = await ResolveActorUserIdAsync(store, principal, ct).ConfigureAwait(false),
        };
        await store.RoleAssignments.CreateAsync(assignment, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.RoleAssigned, "RoleAssignment", assignment.Id.ToString(),
            assignment.EnvironmentId, principal,
            new { assignment.AssigneeType, assignment.AssigneeId, assignment.RoleId, assignment.ProjectId }, ct).ConfigureAwait(false);
        return Results.Created($"/api/admin/role-assignments/{assignment.Id}", assignment);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        await store.RoleAssignments.DeleteAsync(id, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.RoleUnassigned, "RoleAssignment", id.ToString(), null, principal, null, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<Guid?> ResolveActorUserIdAsync(StorageFacade store, ClaimsPrincipal principal, CancellationToken ct)
    {
        var identifier = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }
        var user = await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
        return user?.Id;
    }
}

/// <summary>Inbound shape for POST on the admin role-assignments endpoint.</summary>
public sealed record RoleAssignmentWriteRequest(
    AssigneeType AssigneeType,
    Guid AssigneeId,
    Guid RoleId,
    Guid ProjectId = default,
    Guid? EnvironmentId = null);
