using System.Security.Claims;
using System.Text.Json;
using Featly.Server.Approval;
using Featly.Server.Authentication;
using Featly.Server.Events;
using Featly.Server.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Change-request lifecycle endpoints (ARCHITECTURE.md §12): propose a change,
/// discuss it, submit approval decisions, and apply it once the environment's
/// <see cref="ApprovalPolicy"/> is satisfied — or apply immediately via
/// emergency bypass. The transparent gating that routes ordinary mutations
/// into this flow lands in M8 PR 8C; here the propose endpoint is explicit.
/// </summary>
internal static class AdminChangesEndpoints
{
    public static RouteGroupBuilder MapAdminChanges(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/changes").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/", ListAsync).WithName("Featly.Admin.Changes.List").RequirePermission(Permission.ChangeRead);
        admin.MapGet("/{id:guid}", GetAsync).WithName("Featly.Admin.Changes.Get").RequirePermission(Permission.ChangeRead);
        admin.MapPost("/", ProposeAsync).WithName("Featly.Admin.Changes.Propose").RequirePermission(Permission.ChangeCreate);
        admin.MapPost("/{id:guid}/comments", CommentAsync).WithName("Featly.Admin.Changes.Comment").RequirePermission(Permission.ChangeRead);
        admin.MapPost("/{id:guid}/approvals", DecideAsync).WithName("Featly.Admin.Changes.Decide").RequirePermission(Permission.ChangeApprove);
        admin.MapPost("/{id:guid}/apply", ApplyAsync).WithName("Featly.Admin.Changes.Apply").RequirePermission(Permission.ChangeApply);
        admin.MapPost("/{id:guid}/bypass", BypassAsync).WithName("Featly.Admin.Changes.Bypass").RequirePermission(Permission.ChangeBypass);
        admin.MapPatch("/{id:guid}/schedule", ScheduleAsync).WithName("Featly.Admin.Changes.Schedule").RequirePermission(Permission.ChangeApply);

        return group;
    }

    private static async Task<IResult> ListAsync(StorageFacade store, string? status, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ChangeStatus>(status, ignoreCase: true, out var parsed))
        {
            return Results.Ok(await store.PendingChanges.ListByStatusAsync(parsed, ct).ConfigureAwait(false));
        }
        return Results.Ok(await store.PendingChanges.ListAsync(ct).ConfigureAwait(false));
    }

    private static async Task<IResult> GetAsync(Guid id, StorageFacade store, CancellationToken ct)
    {
        var change = await store.PendingChanges.GetByIdAsync(id, ct).ConfigureAwait(false);
        return change is null ? Results.NotFound(new { error = $"Change '{id}' not found." }) : Results.Ok(change);
    }

    private static async Task<IResult> ProposeAsync(
        ChangeProposeRequest body,
        StorageFacade store,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.EntityType) || string.IsNullOrWhiteSpace(body.EntityKey))
        {
            return Results.BadRequest(new { error = "entityType and entityKey are required." });
        }
        if (!ChangeApplicationService.IsSupported(body.EntityType))
        {
            return Results.BadRequest(new { error = $"Unsupported entityType '{body.EntityType}'." });
        }

        var author = await ChangeActor.ResolveOrCreateAsync(store, principal, ct).ConfigureAwait(false);
        if (author is null)
        {
            return Results.Problem(detail: "Could not resolve the proposing user from the request identity.", statusCode: StatusCodes.Status400BadRequest);
        }

        var environment = await ResolveEnvironmentAsync(store, body.EnvironmentKey, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Results.NotFound(new { error = $"Environment '{body.EnvironmentKey}' not found." });
        }
        if (environment.ReadOnly)
        {
            return Results.Problem(detail: "Environment is ReadOnly.", statusCode: StatusCodes.Status403Forbidden);
        }

        var now = DateTimeOffset.UtcNow;
        var change = new PendingChange
        {
            Id = Guid.NewGuid(),
            EntityType = body.EntityType,
            EntityKey = body.EntityKey,
            EnvironmentId = environment.Id,
            Action = body.Action,
            ProposedState = body.ProposedState.Clone(),
            CurrentState = await ChangeStaleness.CaptureAsync(store, body.EntityType, body.EntityKey, environment.Id, ct).ConfigureAwait(false),
            AuthorUserId = author.Id,
            AuthorMessage = body.Message,
            Status = ChangeStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.PendingChanges.CreateAsync(change, ct).ConfigureAwait(false);
        return Results.Created($"/api/admin/changes/{change.Id}", change);
    }

    private static async Task<IResult> CommentAsync(
        Guid id,
        CommentRequest body,
        StorageFacade store,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var change = await store.PendingChanges.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Results.NotFound(new { error = $"Change '{id}' not found." });
        }
        if (string.IsNullOrWhiteSpace(body.Body))
        {
            return Results.BadRequest(new { error = "body is required." });
        }

        var author = await ChangeActor.ResolveOrCreateAsync(store, principal, ct).ConfigureAwait(false);
        if (author is null)
        {
            return Results.Problem(detail: "Could not resolve the commenting user.", statusCode: StatusCodes.Status400BadRequest);
        }

        change.Comments.Add(new ChangeComment
        {
            Id = Guid.NewGuid(),
            PendingChangeId = change.Id,
            AuthorUserId = author.Id,
            Body = body.Body,
            At = DateTimeOffset.UtcNow,
        });
        change.UpdatedAt = DateTimeOffset.UtcNow;
        await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);
        return Results.Ok(change);
    }

    private static async Task<IResult> DecideAsync(
        Guid id,
        DecisionWriteRequest body,
        StorageFacade store,
        IFeatlyEventPublisher events,
        Settings.IFeatlySettingsProvider settings,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var change = await store.PendingChanges.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Results.NotFound(new { error = $"Change '{id}' not found." });
        }
        if (change.Status != ChangeStatus.Pending)
        {
            return Results.Conflict(new { error = $"Change is {change.Status}; only pending changes accept decisions." });
        }

        var approver = await ChangeActor.ResolveOrCreateAsync(store, principal, ct).ConfigureAwait(false);
        if (approver is null)
        {
            return Results.Problem(detail: "Could not resolve the approving user.", statusCode: StatusCodes.Status400BadRequest);
        }

        change.Approvals.Add(new ChangeApproval
        {
            Id = Guid.NewGuid(),
            PendingChangeId = change.Id,
            ApproverUserId = approver.Id,
            Decision = body.Decision,
            Comment = body.Comment,
            At = DateTimeOffset.UtcNow,
        });
        change.UpdatedAt = DateTimeOffset.UtcNow;

        var policy = await store.ApprovalPolicies.GetByEnvironmentAsync(change.EnvironmentId, ct).ConfigureAwait(false)
            ?? await DefaultPolicyAsync(store, settings, change.EnvironmentId, ct).ConfigureAwait(false);

        var matcher = await ApproverMatcher.BuildAsync(
            store,
            change.Approvals.Where(a => a.Decision == ApprovalDecision.Approve).Select(a => a.ApproverUserId),
            ct).ConfigureAwait(false);
        var evaluation = ApprovalPolicyEvaluator.Evaluate(policy, change, matcher);

        if (evaluation.Rejected)
        {
            change.Status = ChangeStatus.Rejected;
            change.RejectedAt = DateTimeOffset.UtcNow;
            change.RejectionReason = body.Comment;
        }
        else if (evaluation.Satisfied)
        {
            change.Status = ChangeStatus.Approved;
        }

        await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);

        if (change.Status == ChangeStatus.Approved)
        {
            await events.PublishAsync(FeatlyEventTypes.ChangeApproved, "Change", change.Id.ToString(), change.EnvironmentId, principal,
                new { change.Id, change.EntityType, change.EntityKey, change.Action }, ct).ConfigureAwait(false);
        }
        else if (change.Status == ChangeStatus.Rejected)
        {
            await events.PublishAsync(FeatlyEventTypes.ChangeRejected, "Change", change.Id.ToString(), change.EnvironmentId, principal,
                new { change.Id, change.EntityType, change.EntityKey, change.Action }, ct).ConfigureAwait(false);
        }

        return Results.Ok(new { change, evaluation });
    }

    private static async Task<IResult> ApplyAsync(
        Guid id,
        StorageFacade store,
        ChangeApplicationService applier,
        IFeatlyEventPublisher events,
        FeatlyServerMetrics metrics,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var change = await store.PendingChanges.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Results.NotFound(new { error = $"Change '{id}' not found." });
        }
        if (change.Status != ChangeStatus.Approved)
        {
            return Results.Conflict(new { error = $"Change is {change.Status}; only approved changes can be applied." });
        }

        // Stale check: the underlying entity must not have changed since propose.
        if (await ChangeStaleness.IsStaleAsync(store, change, ct).ConfigureAwait(false))
        {
            change.Status = ChangeStatus.Stale;
            change.UpdatedAt = DateTimeOffset.UtcNow;
            await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);
            return Results.Conflict(new { error = "The target entity changed since this change was proposed. Re-propose to rebase.", status = "Stale" });
        }

        var actor = principal.Identity?.Name ?? "anonymous";
        var applied = await applier.ApplyAsync(change, actor, ct).ConfigureAwait(false);
        if (!applied)
        {
            return Results.Problem(detail: $"Could not apply change of type '{change.EntityType}'.", statusCode: StatusCodes.Status500InternalServerError);
        }

        change.Status = ChangeStatus.Applied;
        change.AppliedByUserId = (await ChangeActor.ResolveOrCreateAsync(store, principal, ct).ConfigureAwait(false))?.Id;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.UpdatedAt = DateTimeOffset.UtcNow;
        await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);
        await ChangeStaleness.MarkSiblingsStaleAsync(store, change, ct).ConfigureAwait(false);
        metrics.RecordChangeApplied(change.Action, bypassed: false);
        await events.PublishAsync(FeatlyEventTypes.ChangeApplied, "Change", change.Id.ToString(), change.EnvironmentId, principal,
            new { change.Id, change.EntityType, change.EntityKey, change.Action, emergency = false }, ct).ConfigureAwait(false);
        return Results.Ok(change);
    }

    /// <summary>
    /// Schedules, reschedules, or cancels (<c>scheduledApplyAt: null</c>) an
    /// automatic apply for an approved change (ADR-0028). Gated by the same
    /// <see cref="Permission.ChangeApply"/> permission as manual Apply —
    /// scheduling is "apply, but later," not a distinct capability. Actually
    /// applying the change is <see cref="Approval.ScheduledApplyWorker"/>'s
    /// job once <see cref="PendingChange.ScheduledApplyAt"/> is due.
    /// </summary>
    private static async Task<IResult> ScheduleAsync(
        Guid id,
        ScheduleChangeRequest body,
        StorageFacade store,
        IFeatlyEventPublisher events,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var change = await store.PendingChanges.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Results.NotFound(new { error = $"Change '{id}' not found." });
        }
        if (change.Status != ChangeStatus.Approved)
        {
            return Results.Conflict(new { error = $"Change is {change.Status}; only approved changes can be scheduled." });
        }

        change.ScheduledApplyAt = body.ScheduledApplyAt;
        change.UpdatedAt = DateTimeOffset.UtcNow;
        await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);

        await events.PublishAsync(FeatlyEventTypes.ChangeScheduled, "Change", change.Id.ToString(), change.EnvironmentId, principal,
            new { change.Id, change.EntityType, change.EntityKey, change.Action, scheduledApplyAt = body.ScheduledApplyAt }, ct).ConfigureAwait(false);

        return Results.Ok(change);
    }

    private static async Task<IResult> BypassAsync(
        Guid id,
        BypassRequest body,
        StorageFacade store,
        ChangeApplicationService applier,
        IFeatlyEventPublisher events,
        FeatlyServerMetrics metrics,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var change = await store.PendingChanges.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (change is null)
        {
            return Results.NotFound(new { error = $"Change '{id}' not found." });
        }
        if (change.Status is not (ChangeStatus.Pending or ChangeStatus.Approved))
        {
            return Results.Conflict(new { error = $"Change is {change.Status}; cannot bypass." });
        }
        if (string.IsNullOrWhiteSpace(body.Reason))
        {
            return Results.BadRequest(new { error = "reason is required for an emergency bypass." });
        }

        var policy = await store.ApprovalPolicies.GetByEnvironmentAsync(change.EnvironmentId, ct).ConfigureAwait(false);
        if (policy is not null && !policy.AllowEmergencyBypass)
        {
            return Results.Problem(detail: "Emergency bypass is disabled for this environment.", statusCode: StatusCodes.Status403Forbidden);
        }

        var actor = principal.Identity?.Name ?? "anonymous";
        var applied = await applier.ApplyAsync(change, actor, ct).ConfigureAwait(false);
        if (!applied)
        {
            return Results.Problem(detail: $"Could not apply change of type '{change.EntityType}'.", statusCode: StatusCodes.Status500InternalServerError);
        }

        change.Status = ChangeStatus.Applied;
        change.WasEmergencyBypass = true;
        change.EmergencyReason = body.Reason;
        change.AppliedByUserId = (await ChangeActor.ResolveOrCreateAsync(store, principal, ct).ConfigureAwait(false))?.Id;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.UpdatedAt = DateTimeOffset.UtcNow;
        await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);
        metrics.RecordChangeApplied(change.Action, bypassed: true);
        await events.PublishAsync(FeatlyEventTypes.ChangeApplied, "Change", change.Id.ToString(), change.EnvironmentId, principal,
            new { change.Id, change.EntityType, change.EntityKey, change.Action, emergency = true }, ct).ConfigureAwait(false);
        return Results.Ok(change);
    }

    // Fallback policy for an environment with no explicit ApprovalPolicy — the
    // DB-overridable default template for the environment (ARCHITECTURE.md §15).
    private static async Task<ApprovalPolicy> DefaultPolicyAsync(
        StorageFacade store, Settings.IFeatlySettingsProvider settings, Guid environmentId, CancellationToken ct)
    {
        var env = await store.Environments.GetByIdAsync(environmentId, ct).ConfigureAwait(false);
        return settings.ApprovalDefaults.TemplateFor(env?.Key).ToPolicy(environmentId);
    }

    private static async Task<Environment?> ResolveEnvironmentAsync(StorageFacade store, string? envKey, CancellationToken ct)
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
}

/// <summary>Inbound shape for proposing a change.</summary>
public sealed record ChangeProposeRequest(
    string EntityType,
    string EntityKey,
    ChangeAction Action,
    JsonElement ProposedState,
    string? EnvironmentKey = null,
    string? Message = null);

/// <summary>Inbound shape for a comment.</summary>
public sealed record CommentRequest(string Body);

/// <summary>Inbound shape for an approval decision.</summary>
public sealed record DecisionWriteRequest(ApprovalDecision Decision, string? Comment = null);

/// <summary>Inbound shape for an emergency bypass.</summary>
public sealed record BypassRequest(string Reason);

/// <summary>Inbound shape for scheduling (or cancelling, when <c>null</c>) an automatic apply.</summary>
public sealed record ScheduleChangeRequest(DateTimeOffset? ScheduledApplyAt);
