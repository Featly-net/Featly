using Featly.Server.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Per-environment <see cref="ApprovalPolicy"/> management. The policy decides
/// whether mutations to an environment require approval and what shape that
/// approval takes (<see cref="ApproverRule"/> list, minimum count, self-approval,
/// emergency bypass).
/// </summary>
internal static class AdminApprovalPolicyEndpoints
{
    public static RouteGroupBuilder MapAdminApprovalPolicies(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/approval-policies").RequireAuthorization(FeatlyAuthenticationDefaults.AdminPolicy);

        admin.MapGet("/{env}", GetAsync).WithName("Featly.Admin.ApprovalPolicies.Get").RequirePermission(Permission.ApprovalPolicyRead);
        admin.MapPut("/{env}", UpsertAsync).WithName("Featly.Admin.ApprovalPolicies.Upsert").RequirePermission(Permission.ApprovalPolicyUpdate);
        admin.MapDelete("/{env}", DeleteAsync).WithName("Featly.Admin.ApprovalPolicies.Delete").RequirePermission(Permission.ApprovalPolicyUpdate);

        return group;
    }

    private static async Task<IResult> GetAsync(string env, StorageFacade store, CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.NotFound($"Environment '{env}' not found.");
        }

        var policy = await store.ApprovalPolicies.GetByEnvironmentAsync(environment.Id, ct).ConfigureAwait(false);
        // No policy configured == approval not required. Return a representation
        // rather than 404 so the dashboard editor has something to bind to.
        return Results.Ok(policy ?? new ApprovalPolicy { Id = Guid.Empty, EnvironmentId = environment.Id, Required = false });
    }

    private static async Task<IResult> UpsertAsync(string env, ApprovalPolicyWriteRequest body, StorageFacade store, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.NotFound($"Environment '{env}' not found.");
        }

        var existing = await store.ApprovalPolicies.GetByEnvironmentAsync(environment.Id, ct).ConfigureAwait(false);
        var policy = new ApprovalPolicy
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            EnvironmentId = environment.Id,
            Required = body.Required,
            MinApprovals = body.MinApprovals < 1 ? 1 : body.MinApprovals,
            AuthorCanApproveOwnChange = body.AuthorCanApproveOwnChange,
            AllowEmergencyBypass = body.AllowEmergencyBypass,
            ApproverRules = [.. (body.ApproverRules ?? [])],
        };
        await store.ApprovalPolicies.UpsertAsync(policy, ct).ConfigureAwait(false);
        return Results.Ok(policy);
    }

    private static async Task<IResult> DeleteAsync(string env, StorageFacade store, CancellationToken ct)
    {
        var environment = await ResolveEnvironmentAsync(store, env, ct).ConfigureAwait(false);
        if (environment is null)
        {
            return Problems.NotFound($"Environment '{env}' not found.");
        }
        await store.ApprovalPolicies.DeleteByEnvironmentAsync(environment.Id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static Task<Environment?> ResolveEnvironmentAsync(StorageFacade store, string? envKey, CancellationToken ct)
        => EnvironmentResolver.ResolveAsync(store, envKey, ct);
}

/// <summary>Inbound shape for PUT on the approval-policy endpoint.</summary>
public sealed record ApprovalPolicyWriteRequest(
    bool Required,
    int MinApprovals = 1,
    bool AuthorCanApproveOwnChange = false,
    bool AllowEmergencyBypass = true,
    IReadOnlyList<ApproverRule>? ApproverRules = null);
