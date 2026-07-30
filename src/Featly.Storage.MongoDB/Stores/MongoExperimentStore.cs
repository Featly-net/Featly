using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoExperimentStore(MongoFeatlyDatabase database) : IExperimentStore
{
    public async Task<Experiment?> GetByKeyAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.Experiments
            .Find(e => e.EnvironmentId == environmentId && e.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Experiment?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.Experiments
            .Find(e => e.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Experiment>> ListAsync(Guid environmentId, CancellationToken ct) =>
        await database.Experiments
            .Find(e => e.EnvironmentId == environmentId)
            .SortBy(e => e.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Experiment>> ListActiveAsync(Guid environmentId, CancellationToken ct) =>
        await database.Experiments
            .Find(e => e.EnvironmentId == environmentId && e.StartedAt != null && e.StoppedAt == null)
            .SortBy(e => e.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(Guid environmentId, Experiment experiment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        experiment.UpdatedAt = DateTimeOffset.UtcNow;

        var existing = await database.Experiments
            .Find(e => e.EnvironmentId == environmentId && e.Key == experiment.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await database.Experiments.InsertOneAsync(experiment, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        var replacement = new Experiment
        {
            Id = existing.Id,
            Key = existing.Key,
            Name = experiment.Name,
            Hypothesis = experiment.Hypothesis,
            FlagKey = existing.FlagKey,
            MetricKeys = experiment.MetricKeys,
            StickyAssignments = experiment.StickyAssignments,
            StartedAt = experiment.StartedAt,
            StoppedAt = experiment.StoppedAt,
            EnvironmentId = existing.EnvironmentId,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = experiment.UpdatedAt,
        };

        await database.Experiments.ReplaceOneAsync(e => e.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await database.Experiments
            .DeleteOneAsync(e => e.EnvironmentId == environmentId && e.Key == key, ct)
            .ConfigureAwait(false);
    }
}
