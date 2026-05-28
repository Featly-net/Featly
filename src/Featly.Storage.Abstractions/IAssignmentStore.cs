namespace Featly.Storage;

/// <summary>
/// Persistence for sticky experiment <see cref="Assignment"/> rows. Only used
/// when <see cref="Experiment.StickyAssignments"/> is set — the first exposure
/// of a subject writes its bucketed variant, and later evaluations read it back.
/// </summary>
public interface IAssignmentStore
{
    /// <summary>Returns the persisted assignment for a subject in an experiment, or <c>null</c>.</summary>
    Task<Assignment?> GetAsync(Guid experimentId, string subjectKey, CancellationToken ct);

    /// <summary>
    /// Persists the assignment if the subject has none yet (first-write-wins);
    /// a second call for the same (experiment, subject) is a no-op.
    /// </summary>
    Task UpsertAsync(Assignment assignment, CancellationToken ct);

    /// <summary>Lists every assignment for an experiment.</summary>
    Task<IReadOnlyList<Assignment>> ListByExperimentAsync(Guid experimentId, CancellationToken ct);
}
