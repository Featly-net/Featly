using Featly.Server.Authentication;
using Featly.Server.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// First-run bootstrap: provisions the very first administrator without an
/// existing credential. Solves the chicken-and-egg of "you need an admin to
/// create the first admin" (ARCHITECTURE.md §10) for deployments that don't set
/// a static <c>AdminApiKey</c> or a <c>BootstrapAdminIdentifier</c>.
/// </summary>
/// <remarks>
/// Intentionally unauthenticated, and guarded so it can run only while the
/// instance has no users at all: the first call creates an admin
/// <see cref="User"/>, grants it the system <c>admin</c> role, and mints an
/// admin <see cref="ApiKey"/> bound to that user (returned once). Every
/// subsequent call returns <c>409 Conflict</c>.
/// </remarks>
internal static class BootstrapEndpoints
{
    public static RouteGroupBuilder MapBootstrap(this RouteGroupBuilder apiGroup)
    {
        // No RequireAuthorization: there is no credential yet. The "zero users"
        // guard below is the gate.
        apiGroup.MapPost("/admin/bootstrap", BootstrapAsync).WithName("Featly.Admin.Bootstrap");
        return apiGroup;
    }

    private static async Task<IResult> BootstrapAsync(
        BootstrapRequest body,
        StorageFacade store,
        ApiKeyHasher hasher,
        IFeatlyEventPublisher events,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Identifier))
        {
            return Results.BadRequest(new { error = "identifier is required." });
        }

        // Guard: only available before any user exists. Once an admin (or any
        // user) is present, normal admin-authenticated minting takes over.
        var existingUsers = await store.Users.ListAsync(ct).ConfigureAwait(false);
        if (existingUsers.Count > 0)
        {
            return Results.Conflict(new { error = "Bootstrap is unavailable: a user already exists. Use POST /api/admin/apikeys with an admin credential." });
        }

        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        var environment = project is null
            ? null
            : await store.Environments.GetDefaultAsync(project.Id, ct).ConfigureAwait(false);
        if (project is null || environment is null)
        {
            return Results.Problem("No default project/environment is provisioned yet; retry once the server has finished starting.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var adminRole = await store.Roles.GetByKeyAsync(SystemRoles.AdminKey, ct).ConfigureAwait(false);
        if (adminRole is null)
        {
            return Results.Problem("System roles are not seeded yet; retry once the server has finished starting.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        const string actor = "bootstrap";
        var now = DateTimeOffset.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Identifier = body.Identifier,
            DisplayName = string.IsNullOrWhiteSpace(body.DisplayName) ? body.Identifier : body.DisplayName,
            Email = body.Identifier.Contains('@', StringComparison.Ordinal) ? body.Identifier : null,
            Disabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actor,
            UpdatedBy = actor,
        };
        await store.Users.UpsertAsync(user, actor, ct).ConfigureAwait(false);
        var created = await store.Users.GetByIdentifierAsync(body.Identifier, ct).ConfigureAwait(false) ?? user;

        // Grant the admin role across all environments in the default project.
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = created.Id,
            ProjectId = project.Id,
            EnvironmentId = null,
            RoleId = adminRole.Id,
            AssignedAt = now,
            AssignedByUserId = created.Id,
        }, ct).ConfigureAwait(false);

        // Mint an admin key bound to the new user.
        var plaintext = hasher.GenerateToken();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = $"bootstrap-admin ({created.Identifier})",
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = environment.Id,
            UserId = created.Id,
            Revoked = false,
            CreatedAt = now,
            CreatedBy = actor,
        };
        await store.ApiKeys.CreateAsync(apiKey, ct).ConfigureAwait(false);

        await events.PublishAsync(new FeatlyDomainEvent
        {
            Type = FeatlyEventTypes.AdminBootstrapped,
            EntityType = "User",
            EntityKey = created.Identifier,
            EnvironmentId = environment.Id,
            ActorIdentifier = created.Identifier,
            At = now,
        }, ct).ConfigureAwait(false);

        var response = new BootstrapResponse(
            Identifier: created.Identifier,
            UserId: created.Id,
            ApiKeyId: apiKey.Id,
            Token: plaintext);
        return Results.Created($"/api/admin/users/{created.Identifier}", response);
    }
}

/// <summary>Inbound shape for <c>POST /api/admin/bootstrap</c>.</summary>
public sealed record BootstrapRequest(string Identifier, string? DisplayName = null);

/// <summary>Outbound shape for the bootstrap response. <see cref="Token"/> is shown once.</summary>
public sealed record BootstrapResponse(
    string Identifier,
    Guid UserId,
    Guid ApiKeyId,
    string Token);
