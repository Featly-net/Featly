using System.Security.Claims;
using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Role upgrade requests (ARCHITECTURE.md §11): a user who lacks the permission
/// to assign themselves a role files a request with a justification; an admin
/// approves it — which mints the corresponding <see cref="RoleAssignment"/> —
/// or rejects it with a reason.
/// </summary>
/// <remarks>
/// Filing (<c>POST /</c>) requires only authentication, since the whole point
/// is that a low-privilege user can ask for more. Listing and deciding are
/// gated on governance permissions. The Admin shortcut — assigning a role
/// directly — lives on the role-assignments endpoint and bypasses this flow.
/// </remarks>
internal static class AdminRoleUpgradeRequestsEndpoints
{
    public static RouteGroupBuilder MapAdminRoleUpgradeRequests(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/role-upgrade-requests").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        // Filing requires only an authenticated identity (no RequirePermission).
        admin.MapPost("/", FileAsync).WithName("Featly.Admin.RoleUpgradeRequests.File");

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.RoleUpgradeRequests.List").RequirePermission(Permission.UserRead);
        admin.MapPost("/{id:guid}/approve", ApproveAsync).WithName("Featly.Admin.RoleUpgradeRequests.Approve").RequirePermission(Permission.UserUpdateRole);
        admin.MapPost("/{id:guid}/reject", RejectAsync).WithName("Featly.Admin.RoleUpgradeRequests.Reject").RequirePermission(Permission.UserUpdateRole);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, string? status, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RoleUpgradeStatus>(status, ignoreCase: true, out var parsed))
        {
            return Results.Ok(await store.RoleUpgradeRequests.ListByStatusAsync(parsed, ct).ConfigureAwait(false));
        }
        return Results.Ok(await store.RoleUpgradeRequests.ListAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> FileAsync(
        RoleUpgradeRequestWriteRequest body,
        StorageFacade store,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var filer = await ResolveOrCreateFilerAsync(store, principal, ct).ConfigureAwait(false);
        if (filer is null)
        {
            return Results.Problem(detail: "Could not resolve the filing user from the request identity.", statusCode: StatusCodes.Status400BadRequest);
        }

        var role = await store.Roles.GetByIdAsync(body.RequestedRoleId, ct).ConfigureAwait(false);
        if (role is null)
        {
            return Results.BadRequest(new { error = $"Role '{body.RequestedRoleId}' not found." });
        }

        var projectId = body.TargetProjectId;
        if (projectId == Guid.Empty)
        {
            var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
            if (project is null)
            {
                return Results.BadRequest(new { error = "No default project to target." });
            }
            projectId = project.Id;
        }

        var request = new RoleUpgradeRequest
        {
            Id = Guid.NewGuid(),
            UserId = filer.Id,
            TargetProjectId = projectId,
            TargetEnvironmentId = body.TargetEnvironmentId,
            RequestedRoleId = body.RequestedRoleId,
            Justification = body.Justification,
            Status = RoleUpgradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.RoleUpgradeRequests.CreateAsync(request, ct).ConfigureAwait(false);
        return Results.Created($"/api/admin/role-upgrade-requests/{request.Id}", request);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        StorageFacade store,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var request = await store.RoleUpgradeRequests.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (request is null)
        {
            return Results.NotFound(new { error = $"Request '{id}' not found." });
        }
        if (request.Status != RoleUpgradeStatus.Pending)
        {
            return Results.Conflict(new { error = $"Request is already {request.Status}." });
        }

        var decider = await ResolveActorUserIdAsync(store, principal, ct).ConfigureAwait(false);

        // Minting the assignment is the whole point of approval.
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = request.UserId,
            ProjectId = request.TargetProjectId,
            EnvironmentId = request.TargetEnvironmentId,
            RoleId = request.RequestedRoleId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = decider,
        }, ct).ConfigureAwait(false);

        request.Status = RoleUpgradeStatus.Approved;
        request.DecidedByUserId = decider;
        request.DecidedAt = DateTimeOffset.UtcNow;
        await store.RoleUpgradeRequests.UpdateAsync(request, ct).ConfigureAwait(false);

        return Results.Ok(request);
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        DecisionRequest? body,
        StorageFacade store,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var request = await store.RoleUpgradeRequests.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (request is null)
        {
            return Results.NotFound(new { error = $"Request '{id}' not found." });
        }
        if (request.Status != RoleUpgradeStatus.Pending)
        {
            return Results.Conflict(new { error = $"Request is already {request.Status}." });
        }

        request.Status = RoleUpgradeStatus.Rejected;
        request.DecidedByUserId = await ResolveActorUserIdAsync(store, principal, ct).ConfigureAwait(false);
        request.DecisionComment = body?.Comment;
        request.DecidedAt = DateTimeOffset.UtcNow;
        await store.RoleUpgradeRequests.UpdateAsync(request, ct).ConfigureAwait(false);

        return Results.Ok(request);
    }

    private static async Task<User?> ResolveOrCreateFilerAsync(StorageFacade store, ClaimsPrincipal principal, CancellationToken ct)
    {
        var identifier = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(identifier) || identifier.StartsWith("api-key:", StringComparison.Ordinal))
        {
            return null;
        }

        var existing = await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Identifier = identifier,
            DisplayName = identifier,
            Email = identifier.Contains('@', StringComparison.Ordinal) ? identifier : null,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "role-upgrade-request",
            UpdatedBy = "role-upgrade-request",
        };
        await store.Users.UpsertAsync(user, "role-upgrade-request", ct).ConfigureAwait(false);
        return await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false);
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

/// <summary>Inbound shape for filing a role upgrade request.</summary>
public sealed record RoleUpgradeRequestWriteRequest(
    Guid RequestedRoleId,
    Guid TargetProjectId = default,
    Guid? TargetEnvironmentId = null,
    string? Justification = null);

/// <summary>Optional body carrying a decision comment (used on reject).</summary>
public sealed record DecisionRequest(string? Comment = null);
