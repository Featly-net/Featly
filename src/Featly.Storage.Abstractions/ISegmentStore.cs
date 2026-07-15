namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Segment"/> entities, scoped per environment.
/// </summary>
public interface ISegmentStore
{
    /// <summary>Returns the segment with the given key, or <c>null</c> if missing.</summary>
    Task<Segment?> GetAsync(Guid environmentId, string key, CancellationToken ct);

    /// <summary>Lists all non-archived segments in the environment.</summary>
    Task<IReadOnlyList<Segment>> ListAsync(Guid environmentId, CancellationToken ct);

    /// <summary>Lists all archived segments in the environment.</summary>
    Task<IReadOnlyList<Segment>> ListArchivedAsync(Guid environmentId, CancellationToken ct);

    /// <summary>
    /// Inserts or replaces the segment. Bumps <see cref="Segment.UpdatedAt"/> and
    /// <see cref="Segment.UpdatedBy"/> server-side based on <paramref name="actor"/>.
    /// </summary>
    Task UpsertAsync(Guid environmentId, Segment segment, string actor, CancellationToken ct);

    /// <summary>Removes the segment. Idempotent: missing keys are not an error.</summary>
    Task DeleteAsync(Guid environmentId, string key, string actor, CancellationToken ct);

    /// <summary>Marks the segment as archived. The row is retained and can be restored.</summary>
    Task ArchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct);

    /// <summary>Clears the archived flag, returning the segment to the active list.</summary>
    Task UnarchiveAsync(Guid environmentId, string key, string actor, CancellationToken ct);

}
