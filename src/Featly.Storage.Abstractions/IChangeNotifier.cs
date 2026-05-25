namespace Featly.Storage;

/// <summary>
/// In-process pub/sub for change events. The server's SSE endpoint subscribes
/// to this notifier and fans events out to connected SDK clients.
/// </summary>
/// <remarks>
/// M2 ships an in-process implementation. Multi-instance deployments will
/// add an out-of-process implementation (Postgres LISTEN/NOTIFY, Redis pub/sub)
/// in a later milestone.
/// </remarks>
public interface IChangeNotifier
{
    /// <summary>Publishes a notification that the configuration changed.</summary>
    ValueTask NotifyAsync(ChangeNotification notification, CancellationToken ct);

    /// <summary>
    /// Subscribes to notifications. The returned disposable unsubscribes.
    /// Callbacks may run on a thread pool thread; implementations must not block.
    /// </summary>
    IDisposable Subscribe(Func<ChangeNotification, CancellationToken, ValueTask> handler);
}

/// <summary>
/// Describes a change that consumers of <see cref="IChangeNotifier"/> need to react to.
/// </summary>
/// <param name="EnvironmentId">The affected environment, or <c>null</c> for global changes.</param>
/// <param name="EntityType">Entity affected — for M2 this is always <c>"Flag"</c>.</param>
/// <param name="EntityKey">The key of the affected entity, or <c>null</c> when batched.</param>
/// <param name="At">When the change happened (server clock).</param>
public sealed record ChangeNotification(
    Guid? EnvironmentId,
    string EntityType,
    string? EntityKey,
    DateTimeOffset At);
