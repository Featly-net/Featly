using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresPendingChangeStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IPendingChangeStore
{
    public async Task<PendingChange?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PendingChanges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PendingChanges.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListByStatusAsync(ChangeStatus status, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PendingChanges.AsNoTracking()
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListByEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PendingChanges.AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListOpenForEntityAsync(string entityType, string entityKey, Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PendingChanges.AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId
                && c.EntityType == entityType
                && c.EntityKey == entityKey
                && (c.Status == ChangeStatus.Pending || c.Status == ChangeStatus.Approved))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task CreateAsync(PendingChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.PendingChanges.Add(change);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PendingChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.PendingChanges
            .FirstOrDefaultAsync(c => c.Id == change.Id, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        existing.Status = change.Status;
        existing.AuthorMessage = change.AuthorMessage;
        existing.Approvals = [.. change.Approvals];
        existing.Comments = [.. change.Comments];
        existing.AppliedByUserId = change.AppliedByUserId;
        existing.AppliedAt = change.AppliedAt;
        existing.RejectedAt = change.RejectedAt;
        existing.RejectionReason = change.RejectionReason;
        existing.WasEmergencyBypass = change.WasEmergencyBypass;
        existing.EmergencyReason = change.EmergencyReason;
        existing.ScheduledApplyAt = change.ScheduledApplyAt;
        existing.UpdatedAt = change.UpdatedAt;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
