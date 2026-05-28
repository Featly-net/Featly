using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryApprovalPolicyStore : IApprovalPolicyStore
{
    private readonly ConcurrentDictionary<Guid, ApprovalPolicy> _byEnvironment = new();

    public Task<ApprovalPolicy?> GetByEnvironmentAsync(Guid environmentId, CancellationToken ct)
        => Task.FromResult(_byEnvironment.TryGetValue(environmentId, out var p) ? p : null);

    public Task UpsertAsync(ApprovalPolicy policy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _byEnvironment.AddOrUpdate(
            policy.EnvironmentId,
            _ => policy,
            (_, existing) =>
            {
                existing.Required = policy.Required;
                existing.MinApprovals = policy.MinApprovals;
                existing.AuthorCanApproveOwnChange = policy.AuthorCanApproveOwnChange;
                existing.AllowEmergencyBypass = policy.AllowEmergencyBypass;
                existing.ApproverRules = [.. policy.ApproverRules];
                return existing;
            });
        return Task.CompletedTask;
    }

    public Task DeleteByEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        _byEnvironment.TryRemove(environmentId, out _);
        return Task.CompletedTask;
    }
}
