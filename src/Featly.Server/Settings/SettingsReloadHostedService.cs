using Microsoft.Extensions.Hosting;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Settings;

/// <summary>
/// Loads the settings provider's DB layer at startup and keeps it fresh: it
/// subscribes to the change notifier and reloads whenever a settings-change
/// notification arrives, so an edit on one instance propagates to the cached
/// effective values on every instance (ARCHITECTURE.md §15). For a single
/// embedded instance the subscription simply reloads its own writes.
/// </summary>
/// <remarks>
/// The change notifier already isolates subscriber failures (a throwing handler
/// never takes down the publisher), so the reload handler stays exception-free
/// here rather than wrapping its own catch.
/// </remarks>
internal sealed class SettingsReloadHostedService(
    IFeatlySettingsProvider provider,
    StorageFacade store) : IHostedService
{
    private IDisposable? _subscription;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await provider.ReloadAsync(cancellationToken).ConfigureAwait(false);
        _subscription = store.Changes.Subscribe(OnChangeAsync);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }

    private ValueTask OnChangeAsync(Featly.Storage.ChangeNotification notification, CancellationToken ct)
        => string.Equals(notification.EntityType, FeatlySettingsKeys.ChangeEntityType, StringComparison.Ordinal)
            ? new ValueTask(provider.ReloadAsync(ct))
            : ValueTask.CompletedTask;
}
