using System.Collections.Concurrent;

namespace Featly.Storage.Sqlite.Stores;

/// <summary>
/// In-process implementation of <see cref="IChangeNotifier"/> for the SQLite
/// provider. Identical semantics to the in-memory notifier — duplicated on
/// purpose to keep <c>Featly.Storage.Sqlite</c> independent of
/// <c>Featly.Storage.InMemory</c>. A future refactor may extract a shared
/// "in-process" notifier into <c>Featly.Storage.Abstractions</c>; for now the
/// SQLite provider owns its own copy.
/// </summary>
/// <remarks>
/// Out-of-process notifiers (Postgres LISTEN/NOTIFY, Redis pub/sub) arrive
/// alongside the corresponding storage providers in a later milestone.
/// </remarks>
internal sealed class SqliteChangeNotifier : IChangeNotifier
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
                // Subscriber failures must not take down the publisher.
                // OpenTelemetry tracing lands later and will surface the error.
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

    private sealed class Subscription(SqliteChangeNotifier owner, Guid id) : IDisposable
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
