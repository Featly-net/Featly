using System.Security.Claims;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Admin API endpoints for browsing <see cref="Environment"/> entities and
/// toggling the ReadOnly freeze (M10). The dashboard consumes the list endpoint
/// to populate its environment selector; lock/unlock drives the freeze switch.
/// </summary>
internal static class AdminEnvironmentsEndpoints
{
    public static RouteGroupBuilder MapAdminEnvironments(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/environments").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Environments.List").RequirePermission(Permission.EnvironmentRead);
        admin.MapPost("/", CreateAsync).WithName("Featly.Admin.Environments.Create").RequirePermission(Permission.EnvironmentCreate);
        admin.MapPut("/{key}", UpdateAsync).WithName("Featly.Admin.Environments.Update").RequirePermission(Permission.EnvironmentUpdate);
        admin.MapDelete("/{key}", DeleteAsync).WithName("Featly.Admin.Environments.Delete").RequirePermission(Permission.EnvironmentUpdate);
        admin.MapPost("/{key}/lock", LockAsync).WithName("Featly.Admin.Environments.Lock").RequirePermission(Permission.EnvironmentLock);
        admin.MapPost("/{key}/unlock", UnlockAsync).WithName("Featly.Admin.Environments.Unlock").RequirePermission(Permission.EnvironmentLock);
        admin.MapGet("/{key}/sdk-activity", SdkActivityAsync).WithName("Featly.Admin.Environments.SdkActivity").RequirePermission(Permission.EnvironmentRead);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Results.Ok(Array.Empty<object>());
        }

        var environments = await store.Environments.ListAsync(project.Id, ct).ConfigureAwait(false);
        return Results.Ok(environments);
    }

    private static async Task<IResult> CreateAsync(EnvironmentWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.Key))
        {
            return Problems.Validation("key", "key is required.");
        }

        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Problems.NotFound("No default project.");
        }

        var existing = await store.Environments.GetByKeyAsync(project.Id, body.Key, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Problems.Conflict($"Environment '{body.Key}' already exists.");
        }

        var environment = new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Key = body.Key,
            Name = string.IsNullOrWhiteSpace(body.Name) ? body.Key : body.Name,
            IsDefault = false,
            ReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.Environments.CreateAsync(environment, ct).ConfigureAwait(false);
        return Results.Created($"/api/admin/environments/{environment.Key}", environment);
    }

    private static async Task<IResult> UpdateAsync(string key, EnvironmentWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Problems.NotFound("No default project.");
        }

        var existing = await store.Environments.GetByKeyAsync(project.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Problems.NotFound($"Environment '{key}' not found.");
        }

        existing.Name = string.IsNullOrWhiteSpace(body.Name) ? existing.Name : body.Name;
        await store.Environments.UpdateAsync(existing, ct).ConfigureAwait(false);
        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteAsync(string key, StorageFacade store, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Problems.NotFound("No default project.");
        }

        var existing = await store.Environments.GetByKeyAsync(project.Id, key, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Problems.NotFound($"Environment '{key}' not found.");
        }

        if (existing.IsDefault)
        {
            return Results.Problem(detail: "The default environment cannot be deleted.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Refuse to delete a non-empty environment — that would orphan its content.
        var flags = await store.Flags.ListAsync(existing.Id, ct).ConfigureAwait(false);
        var configs = await store.Configs.ListAsync(existing.Id, ct).ConfigureAwait(false);
        var segments = await store.Segments.ListAsync(existing.Id, ct).ConfigureAwait(false);
        if (flags.Count > 0 || configs.Count > 0 || segments.Count > 0)
        {
            return Problems.Conflict("Environment is not empty; remove its flags, configs and segments first.");
        }

        await store.Environments.DeleteAsync(existing.Id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static Task<IResult> LockAsync(string key, StorageFacade store, IFeatlyEventPublisher events, ClaimsPrincipal user, CancellationToken ct)
        => SetReadOnlyAsync(key, readOnly: true, store, events, user, ct);

    private static Task<IResult> UnlockAsync(string key, StorageFacade store, IFeatlyEventPublisher events, ClaimsPrincipal user, CancellationToken ct)
        => SetReadOnlyAsync(key, readOnly: false, store, events, user, ct);

    private static async Task<IResult> SetReadOnlyAsync(
        string key,
        bool readOnly,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Problems.NotFound("No default project.");
        }

        var environment = await store.Environments.GetByKeyAsync(project.Id, key, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.NotFound($"Environment '{key}' not found.");
        }

        var updated = await store.Environments.SetReadOnlyAsync(environment.Id, readOnly, ct).ConfigureAwait(false);
        await events.PublishAsync(
            readOnly ? FeatlyEventTypes.EnvironmentLocked : FeatlyEventTypes.EnvironmentUnlocked,
            "Environment", environment.Key, environment.Id, user, new { environment.Key, readOnly }, ct).ConfigureAwait(false);

        return Results.Ok(updated);
    }

    // GET /admin/environments/{key}/sdk-activity — in-process, best-effort
    // view of connected SDK clients (see SdkActivityTracker for the
    // multi-replica caveat).
    private static async Task<IResult> SdkActivityAsync(
        string key,
        StorageFacade store,
        Telemetry.SdkActivityTracker activity,
        CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct).ConfigureAwait(false);
        if (project is null)
        {
            return Problems.NotFound("No default project.");
        }

        var environment = await store.Environments.GetByKeyAsync(project.Id, key, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.NotFound($"Environment '{key}' not found.");
        }

        return Results.Ok(activity.GetSnapshot(environment.Id));
    }
}

/// <summary>Inbound shape for POST / PUT on the admin environments endpoint.</summary>
public sealed record EnvironmentWriteRequest(string Key, string? Name);
