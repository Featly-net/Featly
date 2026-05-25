using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

/// <summary>
/// In-process implementation of <see cref="IChangeNotifier"/>. Holds a list
/// of subscriber callbacks and invokes them sequentially on the publisher's
/// thread. Suitable for single-instance deployments.
/// </summary>
internal sealed class InMemoryChangeNotifier : IChangeNotifier
{
    private readonly ConcurrentDictionary<Guid, Func<ChangeNotification, CancellationToken, ValueTask>> _handlers = new();

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
                // Don't let one bad subscriber take down the publisher.
                // Real diagnostics will land alongside the OpenTelemetry work.
            }
        }
    }

    public IDisposable Subscribe(Func<ChangeNotification, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        _handlers[id] = handler;
        return new Subscription(this, id);
    }

    private sealed class Subscription(InMemoryChangeNotifier owner, Guid id) : IDisposable
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
