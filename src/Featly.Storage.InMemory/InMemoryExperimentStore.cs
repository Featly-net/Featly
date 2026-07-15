using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryExperimentStore : IExperimentStore
{
    // Keyed by (environmentId, key).
    private readonly ConcurrentDictionary<(Guid, string), Experiment> _byEnvKey = new();

    public Task<Experiment?> GetByKeyAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_byEnvKey.TryGetValue((environmentId, key), out var e) ? e : null);
    }

    public Task<Experiment?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byEnvKey.Values.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<Experiment>> ListAsync(Guid environmentId, CancellationToken ct)
    {
        var list = _byEnvKey.Values
            .Where(e => e.EnvironmentId == environmentId)
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<Experiment>>(list);
    }

    public Task<IReadOnlyList<Experiment>> ListActiveAsync(Guid environmentId, CancellationToken ct)
    {
        var list = _byEnvKey.Values
            .Where(e => e.EnvironmentId == environmentId && e.IsActive)
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<Experiment>>(list);
    }

    public Task UpsertAsync(Guid environmentId, Experiment experiment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        experiment.UpdatedAt = DateTimeOffset.UtcNow;
        _byEnvKey.AddOrUpdate(
            (environmentId, experiment.Key),
            _ => experiment,
            (_, existing) =>
            {
                existing.Name = experiment.Name;
                existing.Hypothesis = experiment.Hypothesis;
                existing.MetricKeys = [.. experiment.MetricKeys];
                existing.StickyAssignments = experiment.StickyAssignments;
                existing.StartedAt = experiment.StartedAt;
                existing.StoppedAt = experiment.StoppedAt;
                existing.UpdatedAt = experiment.UpdatedAt;
                return existing;
            });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid environmentId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _byEnvKey.TryRemove((environmentId, key), out _);
        return Task.CompletedTask;
    }

}
