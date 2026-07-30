using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoApprovalPolicyStore(MongoFeatlyDatabase database) : IApprovalPolicyStore
{
    public async Task<ApprovalPolicy?> GetByEnvironmentAsync(Guid environmentId, CancellationToken ct) =>
        await database.ApprovalPolicies
            .Find(p => p.EnvironmentId == environmentId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(ApprovalPolicy policy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var existing = await database.ApprovalPolicies
            .Find(p => p.EnvironmentId == policy.EnvironmentId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.ApprovalPolicies.InsertOneAsync(policy, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        var replacement = new ApprovalPolicy
        {
            Id = existing.Id,
            EnvironmentId = existing.EnvironmentId,
            Required = policy.Required,
            MinApprovals = policy.MinApprovals,
            AuthorCanApproveOwnChange = policy.AuthorCanApproveOwnChange,
            AllowEmergencyBypass = policy.AllowEmergencyBypass,
            ApproverRules = policy.ApproverRules,
        };

        await database.ApprovalPolicies.ReplaceOneAsync(p => p.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteByEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        await database.ApprovalPolicies.DeleteOneAsync(p => p.EnvironmentId == environmentId, ct).ConfigureAwait(false);
    }
}
