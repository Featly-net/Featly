using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresAssignmentStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IAssignmentStore
{
    public async Task<Assignment?> GetAsync(Guid experimentId, string subjectKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ExperimentId == experimentId && a.SubjectKey == subjectKey, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(Assignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // First write wins — a subject's assignment never changes once recorded.
        var exists = await db.Assignments.AsNoTracking()
            .AnyAsync(a => a.ExperimentId == assignment.ExperimentId && a.SubjectKey == assignment.SubjectKey, ct)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        db.Assignments.Add(assignment);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent writer won the race against the unique index; the
            // already-persisted assignment stands.
        }
    }

    public async Task<IReadOnlyList<Assignment>> ListByExperimentAsync(Guid experimentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Assignments.AsNoTracking()
            .Where(a => a.ExperimentId == experimentId)
            .OrderBy(a => a.AssignedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
