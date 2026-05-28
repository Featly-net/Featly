namespace Featly.Sdk.Internal;

/// <summary>
/// Non-blocking destination for telemetry events produced on the evaluation hot
/// path. Implementations must never block or throw — a full buffer drops the
/// event rather than back-pressuring the caller.
/// </summary>
internal interface IEventSink
{
    /// <summary>Queues an event for later upload. Returns immediately.</summary>
    void Enqueue(QueuedEvent evt);
}

/// <summary>
/// No-op sink used when the SDK has no server configured (so there is nowhere
/// to flush). Keeps <c>IFlagClient</c> / <c>IEventClient</c> wiring uniform.
/// </summary>
internal sealed class NullEventSink : IEventSink
{
    public void Enqueue(QueuedEvent evt)
    {
        // Intentionally discarded — no server to flush to.
    }
}
