namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Experiment"/> entities. Experiments are
/// scoped to an environment and keyed by <see cref="Experiment.Key"/>.
/// </summary>
public interface IExperimentStore
{
    /// <summary>Returns the experiment with the given key in the environment, or <c>null</c>.</summary>
    Task<Experiment?> GetByKeyAsync(Guid environmentId, string key, CancellationToken ct);

    /// <summary>Returns the experiment with the given row id, or <c>null</c>.</summary>
    Task<Experiment?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lists every experiment in the environment. Sorted by key.</summary>
    Task<IReadOnlyList<Experiment>> ListAsync(Guid environmentId, CancellationToken ct);

    /// <summary>Lists the active experiments (started, not stopped) in the environment — used by the SDK snapshot.</summary>
    Task<IReadOnlyList<Experiment>> ListActiveAsync(Guid environmentId, CancellationToken ct);

    /// <summary>Inserts or updates the experiment matched by environment + key.</summary>
    Task UpsertAsync(Guid environmentId, Experiment experiment, CancellationToken ct);

    /// <summary>Deletes an experiment by key. Idempotent for a missing key.</summary>
    Task DeleteAsync(Guid environmentId, string key, CancellationToken ct);
}
