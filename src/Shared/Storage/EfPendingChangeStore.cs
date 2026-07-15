using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.EntityFramework;

/// <summary>
/// Provider-agnostic <see cref="IPendingChangeStore"/> implemented over EF Core.
/// The relational providers (SQLite, Postgres) derive a one-line subclass bound
/// to their own <typeparamref name="TContext"/>. Compiled into each provider
/// assembly as a linked source file — ADR-0026 keeps the DbContext internal and
/// per-provider, so there is no shared assembly to host this; every query uses
/// <c>Set&lt;PendingChange&gt;()</c> so it stays context-agnostic.
/// </summary>
internal abstract class EfPendingChangeStore<TContext>(IDbContextFactory<TContext> contextFactory) : IPendingChangeStore
    where TContext : DbContext
{
    public async Task<PendingChange?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<PendingChange>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<PendingChange>().AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListByStatusAsync(ChangeStatus status, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<PendingChange>().AsNoTracking()
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListByEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<PendingChange>().AsNoTracking()
            .Where(c => c.EnvironmentId == environmentId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingChange>> ListOpenForEntityAsync(string entityType, string entityKey, Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<PendingChange>().AsNoTracking()
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
        db.Set<PendingChange>().Add(change);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PendingChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<PendingChange>()
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

    /// <summary>
    /// Atomically transitions a change's status with a single conditional
    /// <c>UPDATE ... WHERE status=@from</c> (issue #237). On a shared database
    /// only one concurrent writer's update matches, so exactly one caller claims
    /// the change; the method returns whether this call performed the transition.
    /// </summary>
    public async Task<bool> TryClaimStatusAsync(Guid id, ChangeStatus from, ChangeStatus to, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var affected = await db.Set<PendingChange>()
            .Where(c => c.Id == id && c.Status == from)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Status, to)
                    .SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false);
        return affected == 1;
    }
}
