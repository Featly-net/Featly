namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Segment"/> entities, scoped per environment.
/// </summary>
public interface ISegmentStore
{
    /// <summary>Returns the segment with the given key, or <c>null</c> if missing.</summary>
    Task<Segment?> GetAsync(Guid environmentId, string key, CancellationToken ct);

    /// <summary>Lists every segment in the environment.</summary>
    Task<IReadOnlyList<Segment>> ListAsync(Guid environmentId, CancellationToken ct);

    /// <summary>
    /// Inserts or replaces the segment. Bumps <see cref="Segment.UpdatedAt"/> and
    /// <see cref="Segment.UpdatedBy"/> server-side based on <paramref name="actor"/>.
    /// </summary>
    Task UpsertAsync(Guid environmentId, Segment segment, string actor, CancellationToken ct);

    /// <summary>Removes the segment. Idempotent: missing keys are not an error.</summary>
    Task DeleteAsync(Guid environmentId, string key, string actor, CancellationToken ct);

    /// <summary>
    /// Returns the most recent <see cref="Segment.UpdatedAt"/> across all segments
    /// in the environment, or <c>null</c> when there are none. Folded into the
    /// SDK config endpoint's ETag so segment edits invalidate cached snapshots.
    /// </summary>
    Task<DateTimeOffset?> GetMostRecentUpdateAsync(Guid environmentId, CancellationToken ct);
}
