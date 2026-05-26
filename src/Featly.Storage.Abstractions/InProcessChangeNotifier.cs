using System.Collections.Concurrent;

namespace Featly.Storage;

/// <summary>
/// In-process <see cref="IChangeNotifier"/> implementation backed by a
/// thread-safe handler list. Suitable for single-instance deployments where
/// publisher and subscribers live in the same process — exactly the scenario
/// both the in-memory and SQLite providers ship today.
/// </summary>
/// <remarks>
/// <para>
/// Out-of-process notifiers (Postgres LISTEN/NOTIFY, Redis pub/sub) arrive
/// alongside the corresponding storage providers in a later milestone.
/// </para>
/// <para>
/// Subscriber failures are isolated by design: any exception thrown by a
/// handler is swallowed so a misbehaving subscriber does not take down the
/// publisher. The only exception that propagates is an
/// <see cref="OperationCanceledException"/> tied to the caller's cancellation
/// token. OpenTelemetry tracing in a later milestone will surface the
/// swallowed errors.
/// </para>
/// </remarks>
public sealed class InProcessChangeNotifier : IChangeNotifier
{
    private readonly ConcurrentDictionary<Guid, Func<ChangeNotification, CancellationToken, ValueTask>> _handlers = new();

    /// <inheritdoc />
    public async ValueTask NotifyAsync(ChangeNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        foreach (var handler in _handlers.Values)
        {
            try
            {
                await handler(notification, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Subscriber failures must not take down the publisher.
                // OpenTelemetry tracing lands later and will surface the error.
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Func<ChangeNotification, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        _handlers[id] = handler;
        return new Subscription(this, id);
    }

    private sealed class Subscription(InProcessChangeNotifier owner, Guid id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._handlers.TryRemove(id, out _);
            }
        }
    }
}
