namespace Featly;

/// <summary>
/// Client-side surface for application-tracked telemetry (ARCHITECTURE.md
/// section 16). Implemented by <c>Featly.Sdk</c>: events are enqueued locally
/// and flushed to the server in batches, so calls never block on the network.
/// Automatic <see cref="EventType.Exposure"/> events are emitted by the flag
/// client when an active experiment covers the evaluated flag — applications
/// only need this surface for custom conversion events.
/// </summary>
public interface IEventClient
{
    /// <summary>
    /// Records a custom event (e.g. <c>checkout.completed</c>) for the subject
    /// in the supplied context. The subject is the context's
    /// <see cref="EvaluationContext.TargetingKey"/>; when neither an explicit
    /// nor an ambient context carries one, the event is dropped (nothing to
    /// attribute it to). <paramref name="properties"/> is an optional bag —
    /// an anonymous object or dictionary — serialized to JSON (e.g.
    /// <c>new { revenue = 42.5 }</c>).
    /// </summary>
    ValueTask TrackAsync(
        string eventKey,
        object? properties = null,
        EvaluationContext? context = null,
        CancellationToken ct = default);
}
