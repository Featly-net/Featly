using System.Security.Claims;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Featly.Server.Experiments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ChangeNotification = Featly.Storage.ChangeNotification;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for managing <see cref="Experiment"/> entities and
/// reading their analytics (ARCHITECTURE.md section 16). Env-scoped, behind the
/// admin auth policy with per-route <see cref="Permission"/> enforcement.
/// Mutations emit a <see cref="ChangeNotification"/> so connected SDK clients
/// refresh — a started/stopped experiment changes which evaluations emit
/// exposures. Experiments are not routed through the approval gate; they are a
/// separate concern from flag/config/segment edits.
/// </summary>
internal static class AdminExperimentsEndpoints
{
    public static RouteGroupBuilder MapAdminExperiments(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/experiments").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Experiments.List").RequirePermission(Permission.ExperimentRead);
        admin.MapGet("/{key}", GetAsync).WithName("Featly.Admin.Experiments.Get").RequirePermission(Permission.ExperimentRead);
        admin.MapGet("/{key}/analytics", AnalyticsAsync).WithName("Featly.Admin.Experiments.Analytics").RequirePermission(Permission.ExperimentRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Experiments.Create").RequirePermission(Permission.ExperimentCreate);
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Experiments.Update").RequirePermission(Permission.ExperimentUpdate);
        admin.MapPost("/{key}/start", StartAsync).WithName("Featly.Admin.Experiments.Start").RequirePermission(Permission.ExperimentStart);
        admin.MapPost("/{key}/stop", StopAsync).WithName("Featly.Admin.Experiments.Stop").RequirePermission(Permission.ExperimentStop);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, string? env, CancellationToken ct)
    {
        var environment = await EnvironmentResolver.ResolveAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var experiments = await store.Experiments.ListAsync(environment.Id, ct).ConfigureAwait(false);
        return Results.Ok(experiments);
    }

    private static async Task<IResult> GetAsync(string key, StorageFacade store, string? env, CancellationToken ct)
    {
        var environment = await EnvironmentResolver.ResolveAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var experiment = await store.Experiments.GetByKeyAsync(environment.Id, key, ct).ConfigureAwait(false);
        return experiment is null
            ? Results.NotFound(new { error = $"Experiment '{key}' not found." })
            : Results.Ok(experiment);
    }

    private static async Task<IResult> AnalyticsAsync(string key, StorageFacade store, string? env, CancellationToken ct)
    {
        var environment = await EnvironmentResolver.ResolveAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var experiment = await store.Experiments.GetByKeyAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (experiment is null)
        {
            return Results.NotFound(new { error = $"Experiment '{key}' not found." });
        }

        var exposures = await store.Events
            .QueryAsync(environment.Id, type: EventType.Exposure, flagKey: experiment.FlagKey, ct: ct)
            .ConfigureAwait(false);
        var customEvents = await store.Events
            .QueryAsync(environment.Id, type: EventType.Custom, ct: ct)
            .ConfigureAwait(false);

        // The flag's default variant is the natural control arm for the
        // significance test; a variant with no exposures falls back inside
        // the aggregator to the first observed one.
        var flag = await store.Flags.GetAsync(environment.Id, experiment.FlagKey, ct).ConfigureAwait(false);

        var analytics = ExperimentAnalyticsAggregator.Aggregate(
            experiment, exposures, customEvents, baselineVariantKey: flag?.DefaultVariantKey);
        return Results.Ok(analytics);
    }

    private static async Task<IResult> CreateAsync(
        ExperimentWriteRequest body,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var environment = await EnvironmentResolver.ResolveAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        if (environment.ReadOnly)
        {
            return Results.Problem(detail: "Environment is ReadOnly.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(body.Key) || string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.FlagKey))
        {
            return Results.BadRequest(new { error = "key, name and flagKey are required." });
        }

        var existing = await store.Experiments.GetByKeyAsync(environment.Id, body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Experiment '{body.Key}' already exists in environment '{environment.Key}'." });
        }

        // The experiment is layered on an existing flag in the same environment.
        var flag = await store.Flags.GetAsync(environment.Id, body.FlagKey, ct).ConfigureAwait(false);
        if (flag is null)
        {
            return Results.BadRequest(new { error = $"Flag '{body.FlagKey}' not found in environment '{environment.Key}'." });
        }

        var experiment = body.ToEntity(environment.Id);
        await store.Experiments.UpsertAsync(environment.Id, experiment, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, experiment.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.ExperimentCreated, "Experiment", experiment.Key, environment.Id, user, experiment, ct).ConfigureAwait(false);

        return Results.Created($"/api/admin/experiments/{experiment.Key}?env={environment.Key}", experiment);
    }

    private static async Task<IResult> UpdateAsync(
        string key,
        ExperimentWriteRequest body,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var environment = await EnvironmentResolver.ResolveAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        if (environment.ReadOnly)
        {
            return Results.Problem(detail: "Environment is ReadOnly.", statusCode: StatusCodes.Status403Forbidden);
        }

        var existing = await store.Experiments.GetByKeyAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Experiment '{key}' not found." });
        }

        if (!string.Equals(body.Key, key, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Cannot rename an experiment via PUT. Body key must match URL key." });
        }

        // Name / hypothesis / metric keys / sticky toggle are editable. FlagKey,
        // the time window, and audit fields are managed elsewhere (start/stop).
        existing.Name = body.Name;
        existing.Hypothesis = body.Hypothesis;
        existing.MetricKeys = [.. (body.MetricKeys ?? [])];
        existing.StickyAssignments = body.StickyAssignments;

        await store.Experiments.UpsertAsync(environment.Id, existing, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, existing.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.ExperimentUpdated, "Experiment", existing.Key, environment.Id, user, existing, ct).ConfigureAwait(false);

        return Results.Ok(existing);
    }

    private static async Task<IResult> StartAsync(
        string key,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var environment = await EnvironmentResolver.ResolveAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var experiment = await store.Experiments.GetByKeyAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (experiment is null)
        {
            return Results.NotFound(new { error = $"Experiment '{key}' not found." });
        }

        if (experiment.StartedAt is not null && experiment.StoppedAt is null)
        {
            return Results.Conflict(new { error = $"Experiment '{key}' is already running." });
        }

        // Starting (or restarting a stopped experiment) opens a fresh window.
        experiment.StartedAt = DateTimeOffset.UtcNow;
        experiment.StoppedAt = null;
        await store.Experiments.UpsertAsync(environment.Id, experiment, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, experiment.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.ExperimentStarted, "Experiment", experiment.Key, environment.Id, user, experiment, ct).ConfigureAwait(false);

        return Results.Ok(experiment);
    }

    private static async Task<IResult> StopAsync(
        string key,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var environment = await EnvironmentResolver.ResolveAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var experiment = await store.Experiments.GetByKeyAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (experiment is null)
        {
            return Results.NotFound(new { error = $"Experiment '{key}' not found." });
        }

        if (experiment.StartedAt is null)
        {
            return Results.Conflict(new { error = $"Experiment '{key}' has not been started." });
        }

        if (experiment.StoppedAt is not null)
        {
            return Results.Conflict(new { error = $"Experiment '{key}' is already stopped." });
        }

        experiment.StoppedAt = DateTimeOffset.UtcNow;
        await store.Experiments.UpsertAsync(environment.Id, experiment, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, experiment.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.ExperimentStopped, "Experiment", experiment.Key, environment.Id, user, experiment, ct).ConfigureAwait(false);

        return Results.Ok(experiment);
    }


    private static ValueTask NotifyAsync(StorageFacade store, Guid environmentId, string experimentKey, CancellationToken ct)
        => store.Changes.NotifyAsync(
            new ChangeNotification(environmentId, "Experiment", experimentKey, DateTimeOffset.UtcNow),
            ct);
}

/// <summary>
/// Inbound shape for POST and PUT on the admin experiments endpoint. The time
/// window (<c>startedAt</c>/<c>stoppedAt</c>) is controlled through the
/// dedicated start/stop endpoints, not this body.
/// </summary>
public sealed record ExperimentWriteRequest(
    string Key,
    string Name,
    string? Hypothesis,
    string FlagKey,
    IReadOnlyList<string>? MetricKeys = null,
    bool StickyAssignments = false)
{
    internal Experiment ToEntity(Guid environmentId) => new()
    {
        Id = Guid.NewGuid(),
        Key = Key,
        Name = Name,
        Hypothesis = Hypothesis,
        FlagKey = FlagKey,
        MetricKeys = [.. (MetricKeys ?? [])],
        StickyAssignments = StickyAssignments,
        StartedAt = null,
        StoppedAt = null,
        EnvironmentId = environmentId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
