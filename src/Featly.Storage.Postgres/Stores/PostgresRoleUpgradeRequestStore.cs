using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresRoleUpgradeRequestStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IRoleUpgradeRequestStore
{
    public async Task<RoleUpgradeRequest?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RoleUpgradeRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoleUpgradeRequest>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RoleUpgradeRequests.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoleUpgradeRequest>> ListByStatusAsync(RoleUpgradeStatus status, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.RoleUpgradeRequests.AsNoTracking()
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task CreateAsync(RoleUpgradeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.RoleUpgradeRequests.Add(request);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(RoleUpgradeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.RoleUpgradeRequests
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        existing.Justification = request.Justification;
        existing.Status = request.Status;
        existing.DecidedByUserId = request.DecidedByUserId;
        existing.DecisionComment = request.DecisionComment;
        existing.DecidedAt = request.DecidedAt;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
