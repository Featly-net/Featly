using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresExperimentStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IExperimentStore
{
    public async Task<Experiment?> GetByKeyAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Experiments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EnvironmentId == environmentId && e.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<Experiment?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Experiments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Experiment>> ListAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Experiments.AsNoTracking()
            .Where(e => e.EnvironmentId == environmentId)
            .OrderBy(e => e.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Experiment>> ListActiveAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Experiments.AsNoTracking()
            .Where(e => e.EnvironmentId == environmentId && e.StartedAt != null && e.StoppedAt == null)
            .OrderBy(e => e.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(Guid environmentId, Experiment experiment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        experiment.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Experiments
            .FirstOrDefaultAsync(e => e.EnvironmentId == environmentId && e.Key == experiment.Key, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Experiments.Add(experiment);
        }
        else
        {
            existing.Name = experiment.Name;
            existing.Hypothesis = experiment.Hypothesis;
            existing.MetricKeys = [.. experiment.MetricKeys];
            existing.StickyAssignments = experiment.StickyAssignments;
            existing.StartedAt = experiment.StartedAt;
            existing.StoppedAt = experiment.StoppedAt;
            existing.UpdatedAt = experiment.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Experiments
            .FirstOrDefaultAsync(e => e.EnvironmentId == environmentId && e.Key == key, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        db.Experiments.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Experiments.AsNoTracking()
            .Where(e => e.EnvironmentId == environmentId)
            .Select(e => (DateTimeOffset?)e.UpdatedAt)
            .MaxAsync(ct)
            .ConfigureAwait(false);
    }
}
