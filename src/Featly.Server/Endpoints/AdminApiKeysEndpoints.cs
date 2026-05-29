using System.Security.Claims;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for minting, listing, and revoking <see cref="ApiKey"/>
/// rows. The plaintext token is returned exactly once, at creation; the store
/// keeps only an Argon2id hash. A minted key may be bound to a <see cref="User"/>
/// so that requests authenticated with it attribute to that real identity.
/// </summary>
internal static class AdminApiKeysEndpoints
{
    public static RouteGroupBuilder MapAdminApiKeys(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/apikeys").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.ApiKeys.List").RequirePermission(Permission.ApiKeyRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.ApiKeys.Create").RequirePermission(Permission.ApiKeyCreate);
        admin.MapPost("/{id:guid}/revoke", RevokeAsync).WithName("Featly.Admin.ApiKeys.Revoke").RequirePermission(Permission.ApiKeyRevoke);

        return group;
    }

    // GET /admin/apikeys?environmentKey=  — metadata only, never the hash or token.
    private static async Task<IResult> ListAsync(
        string? environmentKey,
        StorageFacade store,
        CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, environmentKey, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = "No matching environment found." });
        }

        var keys = await store.ApiKeys.ListAsync(environment.Id, ct).ConfigureAwait(false);
        return Results.Ok(keys.Select(ApiKeyView.From).ToList());
    }

    private static async Task<IResult> CreateAsync(
        ApiKeyMintRequest body,
        StorageFacade store,
        ApiKeyHasher hasher,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return Results.BadRequest(new { error = "name is required." });
        }

        var scope = ApiKeyScope.AdminWrite;
        if (!string.IsNullOrWhiteSpace(body.Scope) &&
            !Enum.TryParse(body.Scope, ignoreCase: true, out scope))
        {
            return Results.BadRequest(new { error = $"scope must be one of: {string.Join(", ", Enum.GetNames<ApiKeyScope>())}." });
        }

        var environment = await ResolveEnvironmentAsync(store, body.EnvironmentKey, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.BadRequest(new { error = "No matching environment found; create a project + environment first." });
        }

        var actor = ResolveActor(principal);

        // Optionally bind the key to a real user (auto-creating the user row on
        // first sight, mirroring the change-workflow auto-provision).
        Guid? userId = null;
        if (!string.IsNullOrWhiteSpace(body.UserIdentifier))
        {
            var user = await ResolveOrCreateUserAsync(store, body.UserIdentifier, actor, ct).ConfigureAwait(false);
            userId = user.Id;
        }

        var plaintext = hasher.GenerateToken();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = body.Name,
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = scope,
            EnvironmentId = environment.Id,
            UserId = userId,
            Revoked = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = actor,
        };
        await store.ApiKeys.CreateAsync(apiKey, ct).ConfigureAwait(false);

        await events.PublishAsync(
            FeatlyEventTypes.ApiKeyCreated,
            entityType: "ApiKey",
            entityKey: apiKey.Id.ToString(),
            environmentId: environment.Id,
            user: principal,
            data: new { apiKey.Id, apiKey.Name, Scope = scope.ToString(), apiKey.UserId, apiKey.Prefix },
            ct).ConfigureAwait(false);

        // The plaintext token is shown exactly once.
        var response = ApiKeyMintResponse.From(apiKey, plaintext);
        return Results.Created($"/api/admin/apikeys/{apiKey.Id}", response);
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var existing = await store.ApiKeys.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"API key '{id}' not found." });
        }

        await store.ApiKeys.RevokeAsync(id, ResolveActor(principal), ct).ConfigureAwait(false);
        await events.PublishAsync(
            FeatlyEventTypes.ApiKeyRevoked,
            entityType: "ApiKey",
            entityKey: id.ToString(),
            environmentId: existing.EnvironmentId,
            user: principal,
            data: new { existing.Id, existing.Name },
            ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    /// <summary>
    /// Resolves the environment a key scopes to: the named one inside the default
    /// project, or the project's default environment when no key is given.
    /// </summary>
    internal static async Task<Environment?> ResolveEnvironmentAsync(
        StorageFacade store,
        string? environmentKey,
        CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(environmentKey)
            ? await store.Environments.GetDefaultAsync(project.Id, ct).ConfigureAwait(false)
            : await store.Environments.GetByKeyAsync(project.Id, environmentKey, ct).ConfigureAwait(false);
    }

    internal static async Task<User> ResolveOrCreateUserAsync(
        StorageFacade store,
        string identifier,
        string actor,
        CancellationToken ct)
    {
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
            Disabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actor,
            UpdatedBy = actor,
        };
        await store.Users.UpsertAsync(user, actor, ct).ConfigureAwait(false);
        return (await store.Users.GetByIdentifierAsync(identifier, ct).ConfigureAwait(false)) ?? user;
    }

    private static string ResolveActor(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        return string.IsNullOrEmpty(name) ? "anonymous" : name;
    }
}

/// <summary>Inbound shape for <c>POST /api/admin/apikeys</c>.</summary>
public sealed record ApiKeyMintRequest(
    string Name,
    string? Scope = null,
    string? UserIdentifier = null,
    string? EnvironmentKey = null);

/// <summary>Metadata view of an API key — never includes the hash or plaintext.</summary>
public sealed record ApiKeyView(
    Guid Id,
    string Name,
    string Prefix,
    string Scope,
    Guid EnvironmentId,
    Guid? UserId,
    bool Revoked,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt)
{
    /// <summary>Projects an <see cref="ApiKey"/> to its metadata view.</summary>
    public static ApiKeyView From(ApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ApiKeyView(
            key.Id, key.Name, key.Prefix, key.Scope.ToString(),
            key.EnvironmentId, key.UserId, key.Revoked, key.CreatedAt, key.LastUsedAt);
    }
}

/// <summary>Outbound shape for a freshly minted key. <see cref="Token"/> is shown once.</summary>
public sealed record ApiKeyMintResponse(
    Guid Id,
    string Name,
    string Prefix,
    string Scope,
    Guid EnvironmentId,
    Guid? UserId,
    string Token)
{
    /// <summary>Builds the one-time response carrying the plaintext token.</summary>
    public static ApiKeyMintResponse From(ApiKey key, string token)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ApiKeyMintResponse(
            key.Id, key.Name, key.Prefix, key.Scope.ToString(),
            key.EnvironmentId, key.UserId, token);
    }
}
