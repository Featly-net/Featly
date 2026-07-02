using System.Collections.Concurrent;

namespace Featly.Server.Telemetry;

/// <summary>
/// In-process, best-effort tracker of SDK client activity per environment:
/// how many SSE streams are currently connected, and when a client last
/// fetched a config snapshot or opened a stream.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately does <b>not</b> touch the SDK's flag/config evaluation
/// hot path (<c>IsEnabledAsync</c> et al. stay 100% local — ARCHITECTURE.md
/// §1/§2). It only observes the two calls that are already a network
/// round-trip by design: <c>GET /api/sdk/config</c> (the periodic/ETag poll)
/// and <c>GET /api/sdk/stream</c> (the SSE connection). Recording a sync or a
/// connection is O(1) and allocation-free on the steady-state path.
/// </para>
/// <para>
/// Like <c>IChangeNotifier</c>, this state is <b>in-process</b>: with several
/// centralized-server replicas behind a load balancer, each replica only sees
/// the clients connected to it (see the "Scaling out" note in
/// DEPLOYMENT.md). Fine for the embedded and single-replica centralized
/// patterns; a distributed view is a multi-replica-notifier concern.
/// </para>
/// </remarks>
internal sealed class SdkActivityTracker
{
    private readonly ConcurrentDictionary<Guid, EnvironmentState> _byEnvironment = new();

    /// <summary>Records that a client fetched (or 304'd against) the config snapshot for an environment.</summary>
    public void RecordConfigSync(Guid environmentId)
        => State(environmentId).LastConfigSyncAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Records a new SSE stream connection. Dispose the result when the
    /// connection ends to decrement the active count.
    /// </summary>
    public IDisposable RecordStreamConnected(Guid environmentId)
    {
        var state = State(environmentId);
        Interlocked.Increment(ref state.ActiveStreamConnections);
        state.LastStreamConnectedAt = DateTimeOffset.UtcNow;
        return new StreamLease(state);
    }

    /// <summary>Current activity snapshot for an environment. Zero-valued/null when nothing has been observed yet.</summary>
    public SdkActivitySnapshot GetSnapshot(Guid environmentId)
    {
        if (!_byEnvironment.TryGetValue(environmentId, out var state))
        {
            return new SdkActivitySnapshot(0, null, null);
        }

        return new SdkActivitySnapshot(
            Volatile.Read(ref state.ActiveStreamConnections),
            state.LastConfigSyncAt,
            state.LastStreamConnectedAt);
    }

    private EnvironmentState State(Guid environmentId) => _byEnvironment.GetOrAdd(environmentId, static _ => new EnvironmentState());

    // Plain (non-volatile) DateTimeOffset? fields: best-effort telemetry, like
    // ApiKey.LastUsedAt elsewhere — a torn or stale read is harmless here, and
    // DateTimeOffset? (a struct) cannot carry the volatile modifier.
    private sealed class EnvironmentState
    {
        public int ActiveStreamConnections;
        public DateTimeOffset? LastConfigSyncAt;
        public DateTimeOffset? LastStreamConnectedAt;
    }

    private sealed class StreamLease(EnvironmentState state) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref state.ActiveStreamConnections);
            }
        }
    }
}

/// <summary>Point-in-time SDK activity for one environment.</summary>
public sealed record SdkActivitySnapshot(
    int ActiveStreamConnections,
    DateTimeOffset? LastConfigSyncAt,
    DateTimeOffset? LastStreamConnectedAt);
