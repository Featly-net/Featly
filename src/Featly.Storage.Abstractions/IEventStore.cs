namespace Featly.Storage;

/// <summary>
/// Append-only storage for telemetry <see cref="Event"/> rows (exposures and
/// custom events). Analytics endpoints aggregate over these on read — per
/// ARCHITECTURE.md §16, the raw event data stays queryable.
/// </summary>
public interface IEventStore
{
    /// <summary>Appends a batch of events. Ids are assigned by the caller before append.</summary>
    Task AppendAsync(IReadOnlyList<Event> events, CancellationToken ct);

    /// <summary>
    /// Returns events in an environment, optionally filtered by type, flag key,
    /// and/or custom key. Used by the analytics endpoints to gather exposures
    /// and conversions for an experiment.
    /// </summary>
    Task<IReadOnlyList<Event>> QueryAsync(
        Guid environmentId,
        EventType? type = null,
        string? flagKey = null,
        string? customKey = null,
        CancellationToken ct = default);
}
