using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryAssignmentStore : IAssignmentStore
{
    // Keyed by (experimentId, subjectKey) for first-write-wins semantics.
    private readonly ConcurrentDictionary<(Guid, string), Assignment> _byKey = new();

    public Task<Assignment?> GetAsync(Guid experimentId, string subjectKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        return Task.FromResult(_byKey.TryGetValue((experimentId, subjectKey), out var a) ? a : null);
    }

    public Task UpsertAsync(Assignment assignment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        // First write wins — a subject's assignment never changes.
        _byKey.TryAdd((assignment.ExperimentId, assignment.SubjectKey), assignment);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Assignment>> ListByExperimentAsync(Guid experimentId, CancellationToken ct)
    {
        var list = _byKey.Values.Where(a => a.ExperimentId == experimentId).ToList();
        return Task.FromResult<IReadOnlyList<Assignment>>(list);
    }
}
