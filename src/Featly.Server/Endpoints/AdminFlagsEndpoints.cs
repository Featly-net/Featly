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
/// Admin API endpoints for managing <see cref="Flag"/> entities.
/// </summary>
internal static class AdminFlagsEndpoints
{
    public static RouteGroupBuilder MapAdminFlags(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/flags").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListFlagsAsync).WithName("Featly.Admin.Flags.List").RequirePermission(Permission.FlagRead);
        admin.MapGet("/stale", GetStaleFlagsAsync).WithName("Featly.Admin.Flags.Stale").RequirePermission(Permission.FlagRead);
        admin.MapGet("/{key}", GetFlagAsync).WithName("Featly.Admin.Flags.Get").RequirePermission(Permission.FlagRead);
        admin.MapPost("/", CreateFlagAsync).WithName("Featly.Admin.Flags.Create").RequirePermission(Permission.FlagCreate).RequirePayloadLimits();
        admin.MapPut("/{key}", UpdateFlagAsync).WithName("Featly.Admin.Flags.Update").RequirePermission(Permission.FlagUpdate).RequirePayloadLimits();
        admin.MapPost("/{key}/archive", ArchiveFlagAsync).WithName("Featly.Admin.Flags.Archive").RequirePermission(Permission.FlagArchive);
        admin.MapPost("/{key}/unarchive", UnarchiveFlagAsync).WithName("Featly.Admin.Flags.Unarchive").RequirePermission(Permission.FlagArchive);
        admin.MapGet("/{key}/activity", GetFlagActivityAsync).WithName("Featly.Admin.Flags.Activity").RequirePermission(Permission.FlagRead);

        return group;
    }

    private static async Task<IResult> ListFlagsAsync(
        StorageFacade store,
        string? env,
        CancellationToken ct,
        bool archived = false)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var flags = archived
            ? await store.Flags.ListArchivedAsync(environment.Id, ct).ConfigureAwait(false)
            : await store.Flags.ListAsync(environment.Id, ct).ConfigureAwait(false);
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

        if (body.Prerequisites is { Count: > 0 } proposedPrerequisites)
        {
            var allFlags = await AllFlagsIncludingArchivedAsync(store, environment.Id, ct).ConfigureAwait(false);
            var validation = Flags.PrerequisiteValidator.Validate(allFlags, body.Key, proposedPrerequisites);
            if (!validation.IsValid)
            {
                return Results.Conflict(new { error = validation.Error });
            }
        }

        // Approval gate: when the environment requires approval, this returns a
        // 202 PendingChange (or applies via emergency bypass) instead of a
        // direct create. dryRun never mutates.
        var gated = await gate.InterceptAsync("Flag", body.Key, environment, ChangeAction.Create,
            JsonSerializer.SerializeToElement(body, ChangeJson.Options), user, dryRun, emergency, reason, ct).ConfigureAwait(false);
        if (gated.Outcome == GateOutcome.Handled)
        {
            return gated.Response!;
        }

        var actor = ResolveActor(user);
        var flag = body.ToEntity(environment.Id, actor);
        await store.Flags.UpsertAsync(environment.Id, flag, actor, ct).ConfigureAwait(false);
        await NotifyChangeAsync(store, environment.Id, flag.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.FlagCreated, "Flag", flag.Key, environment.Id, user, new { after = flag }, ct).ConfigureAwait(false);

        return Results.Created($"/api/admin/flags/{flag.Key}?env={environment.Key}", flag);
    }

    private static async Task<IResult> UpdateFlagAsync(
        string key,
        FlagWriteRequest body,
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

        if (body.Prerequisites is { Count: > 0 } proposedPrerequisites)
        {
            var allFlags = await AllFlagsIncludingArchivedAsync(store, environment.Id, ct).ConfigureAwait(false);
            var validation = Flags.PrerequisiteValidator.Validate(allFlags, key, proposedPrerequisites);
            if (!validation.IsValid)
            {
                return Results.Conflict(new { error = validation.Error });
            }
        }

        var gated = await gate.InterceptAsync("Flag", key, environment, ChangeAction.Update,
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
        existing.Enabled = body.Enabled;
        existing.DefaultVariantKey = body.DefaultVariantKey;
        existing.Variants = [.. body.Variants];
        existing.Tags = [.. (body.Tags ?? [])];
        existing.Rules = body.Rules is null ? [] : [.. body.Rules];
        existing.Prerequisites = body.Prerequisites is null ? [] : [.. body.Prerequisites];

        await store.Flags.UpsertAsync(environment.Id, existing, actor, ct).ConfigureAwait(false);
        await NotifyChangeAsync(store, environment.Id, existing.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.FlagUpdated, "Flag", existing.Key, environment.Id, user, new { before, after = existing }, ct).ConfigureAwait(false);

        return Results.Ok(existing);
    }

    private static Task<IResult> ArchiveFlagAsync(
        string key,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
        => SetArchivedAsync(key, store, events, env, user, archived: true, ct);

    private static Task<IResult> UnarchiveFlagAsync(
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

        var actor = ResolveActor(user);
        var before = JsonSerializer.SerializeToElement(existing, ChangeJson.Options);
        if (archived)
        {
            await store.Flags.ArchiveAsync(environment.Id, key, actor, ct).ConfigureAwait(false);
        }
        else
        {
            await store.Flags.UnarchiveAsync(environment.Id, key, actor, ct).ConfigureAwait(false);
        }

        var updated = await store.Flags.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        await NotifyChangeAsync(store, environment.Id, key, ct).ConfigureAwait(false);
        await events.PublishAsync(
            archived ? FeatlyEventTypes.FlagArchived : FeatlyEventTypes.FlagUnarchived,
            "Flag", key, environment.Id, user, new { before, after = updated }, ct).ConfigureAwait(false);

        return Results.Ok(updated);
    }

    // GET /admin/flags/stale?staleDays= — cleanup candidates: no targeting
    // rules left, a stalled experiment, or an archived flag whose experiment
    // is still active. Pure aggregation (Flags.StaleFlagAnalyzer) over data
    // already in storage — no new tracking.
    private static async Task<IResult> GetStaleFlagsAsync(
        StorageFacade store,
        string? env,
        CancellationToken ct,
        int staleDays = 30)
    {
        if (staleDays < 1)
        {
            return Results.BadRequest(new { error = "staleDays must be at least 1." });
        }

        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var flags = await store.Flags.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var archived = await store.Flags.ListArchivedAsync(environment.Id, ct).ConfigureAwait(false);
        var experiments = await store.Experiments.ListAsync(environment.Id, ct).ConfigureAwait(false);
        var exposures = await store.Events.QueryAsync(environment.Id, type: EventType.Exposure, ct: ct).ConfigureAwait(false);

        var lastExposureByFlagKey = exposures
            .Where(e => e.FlagKey is not null)
            .GroupBy(e => e.FlagKey!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (DateTimeOffset?)g.Max(e => e.At), StringComparer.Ordinal);

        var candidates = Flags.StaleFlagAnalyzer.FindCandidates(
            [.. flags, .. archived], experiments, lastExposureByFlagKey, TimeSpan.FromDays(staleDays), DateTimeOffset.UtcNow);

        return Results.Ok(candidates);
    }

    // GET /admin/flags/{key}/activity — last exposure + count, aggregated on
    // read from the Event store (ARCHITECTURE.md §16's existing "aggregate on
    // read" pattern; no new tracking). Only meaningful for a flag that has an
    // active or past experiment: plain evaluation never reaches the server
    // (ARCHITECTURE.md §1 — local-first, no network call on the hot path), so
    // there is nothing to report for a flag that was never under experiment.
    private static async Task<IResult> GetFlagActivityAsync(
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

        var exposures = await store.Events
            .QueryAsync(environment.Id, type: EventType.Exposure, flagKey: key, ct: ct)
            .ConfigureAwait(false);

        DateTimeOffset? lastExposureAt = exposures.Count > 0 ? exposures.Max(e => e.At) : null;

        return Results.Ok(new FlagActivityView(key, lastExposureAt, exposures.Count));
    }

    private static async Task<IReadOnlyList<Flag>> AllFlagsIncludingArchivedAsync(
        StorageFacade store, Guid environmentId, CancellationToken ct)
    {
        var active = await store.Flags.ListAsync(environmentId, ct).ConfigureAwait(false);
        var archived = await store.Flags.ListArchivedAsync(environmentId, ct).ConfigureAwait(false);
        return [.. active, .. archived];
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
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<Rule>? Rules = null,
    IReadOnlyList<Prerequisite>? Prerequisites = null)
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
        Rules = Rules is null ? [] : [.. Rules],
        Prerequisites = Prerequisites is null ? [] : [.. Prerequisites],
        EnvironmentId = environmentId,
        Tags = [.. (Tags ?? [])],
        Archived = false,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = actor,
        UpdatedBy = actor,
    };
}

/// <summary>
/// Exposure-derived activity for one flag. <see cref="LastExposureAt"/> and
/// <see cref="TotalExposureEvents"/> are <c>null</c>/<c>0</c> when the flag has
/// never had an active experiment.
/// </summary>
public sealed record FlagActivityView(string FlagKey, DateTimeOffset? LastExposureAt, int TotalExposureEvents);
