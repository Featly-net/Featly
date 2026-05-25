using System.Security.Claims;
using System.Text.Json;
using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ChangeNotification = Featly.Storage.ChangeNotification;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for managing <see cref="Flag"/> entities.
/// </summary>
internal static class AdminFlagsEndpoints
{
    public static RouteGroupBuilder MapAdminFlags(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/flags").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListFlagsAsync).WithName("Featly.Admin.Flags.List");
        admin.MapGet("/{key}", GetFlagAsync).WithName("Featly.Admin.Flags.Get");
        admin.MapPost("/", CreateFlagAsync).WithName("Featly.Admin.Flags.Create");
        admin.MapPut("/{key}", UpdateFlagAsync).WithName("Featly.Admin.Flags.Update");

        return group;
    }

    private static async Task<IResult> ListFlagsAsync(
        StorageFacade store,
        string? env,
        CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var flags = await store.Flags.ListAsync(environment.Id, ct).ConfigureAwait(false);
        return Results.Ok(flags);
    }

    private static async Task<IResult> GetFlagAsync(
        string key,
        StorageFacade store,
        string? env,
        CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var flag = await store.Flags.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        return flag is null ? Results.NotFound(new { error = $"Flag '{key}' not found." }) : Results.Ok(flag);
    }

    private static async Task<IResult> CreateFlagAsync(
        FlagWriteRequest body,
        StorageFacade store,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        if (environment.ReadOnly)
        {
            return Results.Problem(detail: "Environment is ReadOnly.", statusCode: StatusCodes.Status403Forbidden);
        }

        var existing = await store.Flags.GetAsync(environment.Id, body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Flag '{body.Key}' already exists in environment '{environment.Key}'." });
        }

        var actor = ResolveActor(user);
        var flag = body.ToEntity(environment.Id, actor);
        await store.Flags.UpsertAsync(environment.Id, flag, actor, ct).ConfigureAwait(false);
        await NotifyChangeAsync(store, environment.Id, flag.Key, ct).ConfigureAwait(false);

        return Results.Created($"/api/admin/flags/{flag.Key}?env={environment.Key}", flag);
    }

    private static async Task<IResult> UpdateFlagAsync(
        string key,
        FlagWriteRequest body,
        StorageFacade store,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        if (environment.ReadOnly)
        {
            return Results.Problem(detail: "Environment is ReadOnly.", statusCode: StatusCodes.Status403Forbidden);
        }

        var existing = await store.Flags.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Flag '{key}' not found." });
        }

        if (!string.Equals(body.Key, key, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Cannot rename a flag via PUT. Body key must match URL key." });
        }

        var actor = ResolveActor(user);
        existing.Name = body.Name;
        existing.Description = body.Description;
        existing.Type = body.Type;
        existing.Enabled = body.Enabled;
        existing.DefaultVariantKey = body.DefaultVariantKey;
        existing.Variants = [.. body.Variants];
        existing.Tags = [.. (body.Tags ?? [])];

        await store.Flags.UpsertAsync(environment.Id, existing, actor, ct).ConfigureAwait(false);
        await NotifyChangeAsync(store, environment.Id, existing.Key, ct).ConfigureAwait(false);

        return Results.Ok(existing);
    }

    private static async Task<Environment?> ResolveEnvironmentAsync(
        StorageFacade store,
        string? envKey,
        CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(envKey)
            ? await store.Environments.GetDefaultAsync(project.Id, ct).ConfigureAwait(false)
            : await store.Environments.GetByKeyAsync(project.Id, envKey, ct).ConfigureAwait(false);
    }

    private static string ResolveActor(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        return string.IsNullOrEmpty(name) ? "anonymous" : name;
    }

    private static ValueTask NotifyChangeAsync(StorageFacade store, Guid environmentId, string flagKey, CancellationToken ct)
        => store.Changes.NotifyAsync(
            new ChangeNotification(environmentId, "Flag", flagKey, DateTimeOffset.UtcNow),
            ct);
}

/// <summary>
/// Inbound shape for POST and PUT on the admin flags endpoint.
/// Mirrors <see cref="Flag"/> minus server-managed fields (id, audit).
/// </summary>
public sealed record FlagWriteRequest(
    string Key,
    string Name,
    string? Description,
    FlagType Type,
    bool Enabled,
    string DefaultVariantKey,
    IReadOnlyList<Variant> Variants,
    IReadOnlyList<string>? Tags = null)
{
    internal Flag ToEntity(Guid environmentId, string actor) => new()
    {
        Id = Guid.NewGuid(),
        Key = Key,
        Name = Name,
        Description = Description,
        Type = Type,
        Enabled = Enabled,
        DefaultVariantKey = DefaultVariantKey,
        Variants = [.. Variants],
        EnvironmentId = environmentId,
        Tags = [.. (Tags ?? [])],
        Archived = false,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = actor,
        UpdatedBy = actor,
    };
}
