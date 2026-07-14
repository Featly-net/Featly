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
/// Admin API endpoints for managing <see cref="Segment"/> entities. Mirrors
/// <see cref="AdminFlagsEndpoints"/>: env-scoped, requires the admin auth
/// policy, emits a <see cref="ChangeNotification"/> on every mutation so
/// connected SDK clients invalidate their cached snapshots.
/// </summary>
internal static class AdminSegmentsEndpoints
{
    public static RouteGroupBuilder MapAdminSegments(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/segments").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Segments.List").RequirePermission(Permission.SegmentRead);
        admin.MapGet("/{key}", GetAsync).WithName("Featly.Admin.Segments.Get").RequirePermission(Permission.SegmentRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Segments.Create").RequirePermission(Permission.SegmentCreate).RequirePayloadLimits();
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Segments.Update").RequirePermission(Permission.SegmentUpdate).RequirePayloadLimits();
        admin.MapPost("/{key}/archive", ArchiveAsync).WithName("Featly.Admin.Segments.Archive").RequirePermission(Permission.SegmentArchive);
        admin.MapPost("/{key}/unarchive", UnarchiveAsync).WithName("Featly.Admin.Segments.Unarchive").RequirePermission(Permission.SegmentArchive);
        admin.MapDelete("/{key}", DeleteAsync).WithName("Featly.Admin.Segments.Delete").RequirePermission(Permission.SegmentArchive);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, string? env, CancellationToken ct, bool archived = false)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        var segments = archived
            ? await store.Segments.ListArchivedAsync(environment.Id, ct).ConfigureAwait(false)
            : await store.Segments.ListAsync(environment.Id, ct).ConfigureAwait(false);
        return Results.Ok(segments);
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

        var segment = await store.Segments.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        return segment is null ? Results.NotFound(new { error = $"Segment '{key}' not found." }) : Results.Ok(segment);
    }

    private static async Task<IResult> CreateAsync(
        SegmentWriteRequest body,
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

        var existing = await store.Segments.GetAsync(environment.Id, body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Results.Conflict(new { error = $"Segment '{body.Key}' already exists in environment '{environment.Key}'." });
        }

        var gated = await gate.InterceptAsync("Segment", body.Key, environment, ChangeAction.Create,
            JsonSerializer.SerializeToElement(body, ChangeJson.Options), user, dryRun, emergency, reason, ct).ConfigureAwait(false);
        if (gated.Outcome == GateOutcome.Handled)
        {
            return gated.Response!;
        }

        var actor = ResolveActor(user);
        var segment = body.ToEntity(environment.Id, actor);
        await store.Segments.UpsertAsync(environment.Id, segment, actor, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, segment.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.SegmentCreated, "Segment", segment.Key, environment.Id, user, new { after = segment }, ct).ConfigureAwait(false);

        return Results.Created($"/api/admin/segments/{segment.Key}?env={environment.Key}", segment);
    }

    private static async Task<IResult> UpdateAsync(
        string key,
        SegmentWriteRequest body,
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

        var existing = await store.Segments.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Segment '{key}' not found." });
        }

        if (!string.Equals(body.Key, key, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "Cannot rename a segment via PUT. Body key must match URL key." });
        }

        var gated = await gate.InterceptAsync("Segment", key, environment, ChangeAction.Update,
            JsonSerializer.SerializeToElement(body, ChangeJson.Options), user, dryRun, emergency, reason, ct).ConfigureAwait(false);
        if (gated.Outcome == GateOutcome.Handled)
        {
            return gated.Response!;
        }

        var actor = ResolveActor(user);
        var before = JsonSerializer.SerializeToElement(existing, ChangeJson.Options);
        existing.Name = body.Name;
        existing.Description = body.Description;
        existing.Conditions = [.. body.Conditions];

        await store.Segments.UpsertAsync(environment.Id, existing, actor, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, existing.Key, ct).ConfigureAwait(false);
        await events.PublishAsync(FeatlyEventTypes.SegmentUpdated, "Segment", existing.Key, environment.Id, user, new { before, after = existing }, ct).ConfigureAwait(false);

        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteAsync(
        string key,
        StorageFacade store,
        IFeatlyEventPublisher events,
        string? env,
        ClaimsPrincipal user,
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

        var actor = ResolveActor(user);
        var existing = await store.Segments.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        await store.Segments.DeleteAsync(environment.Id, key, actor, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, key, ct).ConfigureAwait(false);
        object? deletedData = existing is null ? null : new { before = existing };
        await events.PublishAsync(FeatlyEventTypes.SegmentDeleted, "Segment", key, environment.Id, user, deletedData, ct).ConfigureAwait(false);

        return Results.NoContent();
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
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{env}' not found." });
        }

        if (environment.ReadOnly)
        {
            return Results.Problem(detail: "Environment is ReadOnly.", statusCode: StatusCodes.Status403Forbidden);
        }

        var existing = await store.Segments.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Segment '{key}' not found." });
        }

        var actor = ResolveActor(user);
        var before = JsonSerializer.SerializeToElement(existing, ChangeJson.Options);
        if (archived)
        {
            await store.Segments.ArchiveAsync(environment.Id, key, actor, ct).ConfigureAwait(false);
        }
        else
        {
            await store.Segments.UnarchiveAsync(environment.Id, key, actor, ct).ConfigureAwait(false);
        }

        var updated = await store.Segments.GetAsync(environment.Id, key, ct).ConfigureAwait(false);
        await NotifyAsync(store, environment.Id, key, ct).ConfigureAwait(false);
        await events.PublishAsync(
            archived ? FeatlyEventTypes.SegmentArchived : FeatlyEventTypes.SegmentUnarchived,
            "Segment", key, environment.Id, user, new { before, after = updated }, ct).ConfigureAwait(false);

        return Results.Ok(updated);
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

    private static ValueTask NotifyAsync(StorageFacade store, Guid environmentId, string segmentKey, CancellationToken ct)
        => store.Changes.NotifyAsync(
            new ChangeNotification(environmentId, "Segment", segmentKey, DateTimeOffset.UtcNow),
            ct);
}

/// <summary>
/// Inbound shape for POST and PUT on the admin segments endpoint.
/// Mirrors <see cref="Segment"/> minus server-managed fields (id, audit).
/// </summary>
public sealed record SegmentWriteRequest(
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<Condition> Conditions)
{
    internal Segment ToEntity(Guid environmentId, string actor) => new()
    {
        Id = Guid.NewGuid(),
        Key = Key,
        Name = Name,
        Description = Description,
        Conditions = [.. Conditions],
        EnvironmentId = environmentId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedBy = actor,
        UpdatedBy = actor,
    };
}
