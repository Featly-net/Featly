using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Settings;

/// <summary>
/// Loads the settings provider's DB layer at startup and keeps it fresh: it
/// subscribes to the change notifier and reloads whenever a settings-change
/// notification arrives, so an edit on one instance propagates to the cached
/// effective values on every instance (ARCHITECTURE.md §15). For a single
/// embedded instance the subscription simply reloads its own writes.
/// </summary>
internal sealed partial class SettingsReloadHostedService(
    IFeatlySettingsProvider provider,
    StorageFacade store,
    ILogger<SettingsReloadHostedService> logger) : IHostedService
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

    private async ValueTask OnChangeAsync(Featly.Storage.ChangeNotification notification, CancellationToken ct)
    {
        if (!string.Equals(notification.EntityType, FeatlySettingsKeys.ChangeEntityType, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await provider.ReloadAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Reload is best-effort; never let a reload failure break the notifier.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReloadFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 3201, Level = LogLevel.Error, Message = "Failed to reload settings after a change notification.")]
    private static partial void LogReloadFailed(ILogger logger, Exception exception);
}
