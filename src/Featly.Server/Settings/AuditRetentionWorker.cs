using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Settings;

/// <summary>
/// Periodically trims the audit log to the effective retention window
/// (ARCHITECTURE.md §15, the audit half of #101). Disabled by default — when
/// <see cref="FeatlyAuditSettings.RetentionDays"/> is <c>0</c> the log is kept
/// forever, preserving the historical behavior.
/// </summary>
internal sealed partial class AuditRetentionWorker(
    IFeatlySettingsProvider settings,
    StorageFacade store,
    ILogger<AuditRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The trimmer must survive any single-run failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogPruneFailed(logger, ex);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PruneOnceAsync(CancellationToken ct)
    {
        var days = settings.Audit.RetentionDays;
        if (days <= 0)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var removed = await store.Audit.PruneOlderThanAsync(cutoff, ct).ConfigureAwait(false);
        if (removed > 0)
        {
            LogPruned(logger, removed, days);
        }
    }

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information, Message = "Pruned {Count} audit entries older than {Days} days.")]
    private static partial void LogPruned(ILogger logger, int count, int days);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Error, Message = "Audit retention prune failed.")]
    private static partial void LogPruneFailed(ILogger logger, Exception exception);
}
