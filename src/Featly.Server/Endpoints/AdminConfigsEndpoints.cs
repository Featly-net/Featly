using System.Security.Claims;
using System.Text.Json;
using Featly.Server.Approval;
using Featly.Server.Authentication;
using Featly.Server.Events;
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
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Configs.Create").RequirePermission(Permission.ConfigCreate).RequirePayloadLimits();
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Configs.Update").RequirePermission(Permission.ConfigUpdate).RequirePayloadLimits();
        admin.MapPost("/{key}/archive", ArchiveAsync).WithName("Featly.Admin.Configs.Archive").RequirePermission(Permission.ConfigArchive);
        admin.MapPost("/{key}/unarchive", UnarchiveAsync).WithName("Featly.Admin.Configs.Unarchive").RequirePermission(Permission.ConfigArchive);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, string? env, CancellationToken ct, bool archived = false)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.NotFound($"Environment '{env}' not found.");
        }

        var configs = archived
            ? await store.Configs.ListArchivedAsync(environment.Id, ct).ConfigureAwait(false)
            : await store.Configs.ListAsync(environment.Id, ct).ConfigureAwait(false);
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
            return Problems.NotFound($"Environment '{env}' not found.");
        }

        var config = await store.Configs.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        return config is null ? Problems.NotFound($"Config '{key}' not found.") : Results.Ok(config);
    }

    private static async Task<IResult> CreateAsync(
        ConfigWriteRequest body,
        StorageFacade store,
        ChangeGate gate,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool dryRun = false,
        bool emergency = false,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        var (environment, guard) = await EnvironmentResolver.ResolveWritableAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return guard!;
        }

        var existing = await store.Configs.GetAsync(environment.Id, body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Problems.Conflict($"Config '{body.Key}' already exists in environment '{environment.Key}'.");
        }

        var gated = await gate.InterceptAsync("Config", body.Key, environment, ChangeAction.Create,
            JsonSerializer.SerializeToElement(body, ChangeJson.Options), user, dryRun, emergency, reason, ct).ConfigureAwait(false);
        if (gated.Outcome == GateOutcome.Handled)
        {
            return gated.Response!;
        }

        var actor = ResolveActor(user);
        var config = body.ToEntity(environment.Id, actor);
        await store.Configs.UpsertAsync(environment.Id, config, actor, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, config.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.ConfigCreated, "Config", config.Key, environment.Id, user, new { after = config }, ct).ConfigureAwait(false);

        return Results.Created($"/api/admin/configs/{config.Key}?env={environment.Key}", config);
    }

    private static async Task<IResult> UpdateAsync(
        string key,
        ConfigWriteRequest body,
        StorageFacade store,
        ChangeGate gate,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool dryRun = false,
        bool emergency = false,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        var (environment, guard) = await EnvironmentResolver.ResolveWritableAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return guard!;
        }

        var existing = await store.Configs.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Problems.NotFound($"Config '{key}' not found.");
        }

        if (!string.Equals(body.Key, key, StringComparison.Ordinal))
        {
            return Problems.BadRequest("Cannot rename a config via PUT. Body key must match URL key.");
        }

        var gated = await gate.InterceptAsync("Config", key, environment, ChangeAction.Update,
            JsonSerializer.SerializeToElement(body, ChangeJson.Options), user, dryRun, emergency, reason, ct).ConfigureAwait(false);
        if (gated.Outcome == GateOutcome.Handled)
        {
            return gated.Response!;
        }

        var actor = ResolveActor(user);
        var before = JsonSerializer.SerializeToElement(existing, ChangeJson.Options);
        existing.Name = body.Name;
        existing.Description = body.Description;
        existing.Type = body.Type;
        existing.DefaultValue = body.DefaultValue;
        existing.Tags = [.. (body.Tags ?? [])];
        existing.Rules = body.Rules is null ? [] : [.. body.Rules];

        await store.Configs.UpsertAsync(environment.Id, existing, actor, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, existing.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.ConfigUpdated, "Config", existing.Key, environment.Id, user, new { before, after = existing }, ct).ConfigureAwait(false);

        return Results.Ok(existing);
    }

    private static Task<IResult> ArchiveAsync(
        string key,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
        => SetArchivedAsync(key, store, events, env, user, archived: true, ct);

    private static Task<IResult> UnarchiveAsync(
        string key,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
        => SetArchivedAsync(key, store, events, env, user, archived: false, ct);

    private static async Task<IResult> SetArchivedAsync(
        string key,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        bool archived,
        CancellationToken ct)
    {
        var (environment, guard) = await EnvironmentResolver.ResolveWritableAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return guard!;
        }

        var existing = await store.Configs.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Problems.NotFound($"Config '{key}' not found.");
        }

        var actor = ResolveActor(user);
        var before = JsonSerializer.SerializeToElement(existing, ChangeJson.Options);
        if (archived)
        {
            await store.Configs.ArchiveAsync(environment.Id, key, actor, ct).ConfigureAwait(false);
        }
        else
        {
            await store.Configs.UnarchiveAsync(environment.Id, key, actor, ct).ConfigureAwait(false);
        }

        var updated = await store.Configs.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, key, ct).ConfigureAwait(false);
        await events.PublishAsync(
            archived ? FeatlyEventTypes.ConfigArchived : FeatlyEventTypes.ConfigUnarchived,
            "Config", key, environment.Id, user, new { before, after = updated }, ct).ConfigureAwait(false);

        return Results.Ok(updated);
    }

    private static Task<Environment?> ResolveEnvironmentAsync(StorageFacade store, string? envKey, CancellationToken ct)
        => EnvironmentResolver.ResolveAsync(store, envKey, ct);

    private static string ResolveActor(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        return string.IsNullOrEmpty(name) ? "anonymous" : name;
    }

    private static Task NotifyAsync(StorageFacade store, Guid environmentId, string configKey, CancellationToken ct)
        => SnapshotChange.AnnounceAsync(store, environmentId, "Config", configKey, ct);
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
