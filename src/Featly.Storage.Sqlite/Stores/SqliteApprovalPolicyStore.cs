using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteApprovalPolicyStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IApprovalPolicyStore
{
    public async Task<ApprovalPolicy?> GetByEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ApprovalPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EnvironmentId == environmentId, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(ApprovalPolicy policy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.ApprovalPolicies
            .FirstOrDefaultAsync(p => p.EnvironmentId == policy.EnvironmentId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.ApprovalPolicies.Add(policy);
        }
        else
        {
            existing.Required = policy.Required;
            existing.MinApprovals = policy.MinApprovals;
            existing.AuthorCanApproveOwnChange = policy.AuthorCanApproveOwnChange;
            existing.AllowEmergencyBypass = policy.AllowEmergencyBypass;
            existing.ApproverRules = [.. policy.ApproverRules];
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteByEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.ApprovalPolicies
            .FirstOrDefaultAsync(p => p.EnvironmentId == environmentId, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }
        db.ApprovalPolicies.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
