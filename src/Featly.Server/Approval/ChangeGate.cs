using System.Security.Claims;
using System.Text.Json;
using Featly.Server.Endpoints;
using Microsoft.AspNetCore.Http;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Approval;

/// <summary>
/// Transparent approval gate for mutation endpoints (ARCHITECTURE.md §12). The
/// flag / config / segment create + update handlers call
/// <see cref="InterceptAsync"/> before applying. When the target environment's
/// <see cref="ApprovalPolicy"/> requires approval, the mutation is turned into a
/// <see cref="PendingChange"/> (202) instead of applying — unless it's an
/// emergency bypass, which applies immediately with an audit trail. A
/// <c>?dryRun=true</c> request never mutates and just reports whether approval
/// would be required.
/// </summary>
internal sealed class ChangeGate(StorageFacade store, ChangeApplicationService applier, Settings.IFeatlySettingsProvider settings)
{
    /// <summary>
    /// Decides how a mutation should proceed. When <see cref="GateResult.Outcome"/>
    /// is <see cref="GateOutcome.Handled"/> the caller returns
    /// <see cref="GateResult.Response"/> as-is; when it is
    /// <see cref="GateOutcome.ApplyDirectly"/> the caller performs its normal
    /// direct mutation.
    /// </summary>
    public async Task<GateResult> InterceptAsync(
        string entityType,
        string entityKey,
        Environment environment,
        ChangeAction action,
        JsonElement proposedState,
        ClaimsPrincipal principal,
        bool dryRun,
        bool emergency,
        string? reason,
        CancellationToken ct)
    {
        // Explicit per-environment policy wins; otherwise fall back to the
        // DB-overridable default template for this environment (ARCHITECTURE.md §15).
        var policy = await store.ApprovalPolicies.GetByEnvironmentAsync(environment.Id, ct).ConfigureAwait(false)
            ?? settings.ApprovalDefaults.TemplateFor(environment.Key).ToPolicy(environment.Id);
        var requiresApproval = policy is { Required: true };

        if (dryRun)
        {
            return GateResult.Handled(Results.Ok(new
            {
                dryRun = true,
                wouldRequireApproval = requiresApproval,
                entityType,
                entityKey,
                action = action.ToString(),
            }));
        }

        if (!requiresApproval)
        {
            return GateResult.ApplyDirectly;
        }

        var currentState = await ChangeStaleness.CaptureAsync(store, entityType, entityKey, environment.Id, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        if (emergency)
        {
            if (policy is { AllowEmergencyBypass: false })
            {
                return GateResult.Handled(Results.Problem(detail: "Emergency bypass is disabled for this environment.", statusCode: StatusCodes.Status403Forbidden));
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return GateResult.Handled(Results.BadRequest(new { error = "reason is required for an emergency bypass (?emergency=true&reason=...)." }));
            }

            var actorUser = await ChangeActor.ResolveOrCreateAsync(store, principal, ct).ConfigureAwait(false);
            var bypass = new PendingChange
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                EntityKey = entityKey,
                EnvironmentId = environment.Id,
                Action = action,
                ProposedState = proposedState.Clone(),
                CurrentState = currentState,
                AuthorUserId = actorUser?.Id ?? Guid.Empty,
                Status = ChangeStatus.Applied,
                WasEmergencyBypass = true,
                EmergencyReason = reason,
                AppliedByUserId = actorUser?.Id,
                AppliedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await store.PendingChanges.CreateAsync(bypass, ct).ConfigureAwait(false);
            await applier.ApplyAsync(bypass, principal.Identity?.Name ?? "anonymous", ct).ConfigureAwait(false);
            await ChangeStaleness.MarkSiblingsStaleAsync(store, bypass, ct).ConfigureAwait(false);
            return GateResult.Handled(Results.Ok(bypass));
        }

        // Normal gated path: a real human must own the proposal.
        var author = await ChangeActor.ResolveOrCreateAsync(store, principal, ct).ConfigureAwait(false);
        if (author is null)
        {
            return GateResult.Handled(Results.Problem(
                detail: "This environment requires approval; propose the change from a human session rather than a legacy API key.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var change = new PendingChange
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityKey = entityKey,
            EnvironmentId = environment.Id,
            Action = action,
            ProposedState = proposedState.Clone(),
            CurrentState = currentState,
            AuthorUserId = author.Id,
            Status = ChangeStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.PendingChanges.CreateAsync(change, ct).ConfigureAwait(false);
        return GateResult.Handled(Results.Accepted($"/api/admin/changes/{change.Id}", change));
    }
}

/// <summary>Whether the caller should apply its mutation directly or return the gate's response.</summary>
public enum GateOutcome
{
    /// <summary>No approval required — the caller performs its normal mutation.</summary>
    ApplyDirectly,

    /// <summary>The gate handled the request (gated, bypassed, or dry-run); return <see cref="GateResult.Response"/>.</summary>
    Handled,
}

/// <summary>Result of <see cref="ChangeGate.InterceptAsync"/>.</summary>
public sealed record GateResult(GateOutcome Outcome, IResult? Response)
{
    /// <summary>The caller should apply its mutation directly.</summary>
    public static readonly GateResult ApplyDirectly = new(GateOutcome.ApplyDirectly, null);

    /// <summary>The gate produced the response to return.</summary>
    public static GateResult Handled(IResult response) => new(GateOutcome.Handled, response);
}
