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
/// Admin API endpoints for managing <see cref="Config"/> entities. Mirrors
/// <see cref="AdminFlagsEndpoints"/>: env-scoped, requires the admin auth
/// policy, emits a <see cref="ChangeNotification"/> on every mutation so
/// connected SDK clients invalidate their cached snapshots.
/// </summary>
internal static class AdminConfigsEndpoints
{
    public static RouteGroupBuilder MapAdminConfigs(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/configs").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Configs.List").RequirePermission(Permission.ConfigRead);
        admin.MapGet("/{key}", GetAsync).WithName("Featly.Admin.Configs.Get").RequirePermission(Permission.ConfigRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Configs.Create").RequirePermission(Permission.ConfigCreate);
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Configs.Update").RequirePermission(Permission.ConfigUpdate);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, string? env, CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var configs = await store.Configs.ListAsync(environment.Id, ct).ConfigureAwait(false);
        return Results.Ok(configs);
    }

    private static async Task<IResult> GetAsync(
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

        var config = await store.Configs.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        return config is null ? Results.NotFound(new { error = $"Config '{key}' not found." }) : Results.Ok(config);
    }

    private static async Task<IResult> CreateAsync(
        ConfigWriteRequest body,
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

        var existing = await store.Configs.GetAsync(environment.Id, body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Config '{body.Key}' already exists in environment '{environment.Key}'." });
        }

        var actor = ResolveActor(user);
        var config = body.ToEntity(environment.Id, actor);
        await store.Configs.UpsertAsync(environment.Id, config, actor, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, config.Key, ct).ConfigureAwait(false);

        return Results.Created($"/api/admin/configs/{config.Key}?env={environment.Key}", config);
    }

    private static async Task<IResult> UpdateAsync(
        string key,
        ConfigWriteRequest body,
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

        var existing = await store.Configs.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Config '{key}' not found." });
        }

        if (!string.Equals(body.Key, key, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Cannot rename a config via PUT. Body key must match URL key." });
        }

        var actor = ResolveActor(user);
        existing.Name = body.Name;
        existing.Description = body.Description;
        existing.Type = body.Type;
        existing.DefaultValue = body.DefaultValue;
        existing.Tags = [.. (body.Tags ?? [])];
        existing.Rules = body.Rules is null ? [] : [.. body.Rules];

        await store.Configs.UpsertAsync(environment.Id, existing, actor, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, existing.Key, ct).ConfigureAwait(false);

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

    private static ValueTask NotifyAsync(StorageFacade store, Guid environmentId, string configKey, CancellationToken ct)
        => store.Changes.NotifyAsync(
            new ChangeNotification(environmentId, "Config", configKey, DateTimeOffset.UtcNow),
            ct);
}

/// <summary>
/// Inbound shape for POST and PUT on the admin configs endpoint.
/// Mirrors <see cref="Config"/> minus server-managed fields (id, audit).
/// </summary>
public sealed record ConfigWriteRequest(
    string Key,
    string Name,
    string? Description,
    ConfigType Type,
    JsonElement DefaultValue,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ConfigRule>? Rules = null)
{
    internal Config ToEntity(Guid environmentId, string actor) => new()
    {
        Id = Guid.NewGuid(),
        Key = Key,
        Name = Name,
        Description = Description,
        Type = Type,
        DefaultValue = DefaultValue,
        Rules = Rules is null ? [] : [.. Rules],
        EnvironmentId = environmentId,
        Tags = [.. (Tags ?? [])],
        Archived = false,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = actor,
        UpdatedBy = actor,
    };
}
