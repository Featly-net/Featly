using System.Net.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Featly.Sdk.Internal;

/// <summary>
/// BackgroundService responsible for keeping <see cref="FeatlySnapshotCache"/>
/// current. Performs an initial fetch at startup; then attempts to keep a
/// long-lived SSE connection open and re-fetches via ETag whenever the
/// server signals a change. Falls back to plain polling when SSE is
/// disabled or the stream cannot be established.
/// </summary>
internal sealed partial class FeatlyConfigSyncService(
    FeatlyHttpClient http,
    FeatlySnapshotCache cache,
    IOptions<FeatlySdkOptions> options,
    ILogger<FeatlyConfigSyncService> logger)
    : BackgroundService
{
    private static readonly TimeSpan s_minBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_maxBackoff = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        // Seed from disk before the first fetch so evaluations have a baseline
        // even offline / on a cold start (issue #238).
        var seededFrom = await FeatlySnapshotFileStore
            .SeedCacheAsync(cache, opts.OfflineCachePath, opts.BootstrapFilePath, stoppingToken)
            .ConfigureAwait(false);
        if (seededFrom is not null)
        {
            LogSeeded(logger, seededFrom, cache.Current.Snapshot!.Flags.Count);
        }

        // Initial fetch — if it fails we still keep trying (and keep serving the
        // seeded snapshot in the meantime).
        await TryRefreshAsync(opts, stoppingToken).ConfigureAwait(false);

        if (opts.EnableStreaming)
        {
            await RunWithStreamingAsync(opts, stoppingToken).ConfigureAwait(false);
        }
        else
        {
            await RunPollingOnlyAsync(opts, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunPollingOnlyAsync(FeatlySdkOptions opts, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(opts.PollingInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await TryRefreshAsync(opts, ct).ConfigureAwait(false);
        }
    }

    private async Task RunWithStreamingAsync(FeatlySdkOptions opts, CancellationToken ct)
    {
        var backoff = s_minBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await http.OpenStreamAsync(opts.EnvironmentKey, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                backoff = s_minBackoff;
                LogStreamConnected(logger);

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        // Server closed the stream — loop and reconnect.
                        break;
                    }

                    if (line.StartsWith("event:", StringComparison.Ordinal) &&
                        line.AsSpan("event:".Length).Trim().Equals("changed", StringComparison.Ordinal))
                    {
                        // Consume the data line that follows (if any) before refreshing.
                        // Naive parser; M3+ will adopt a proper SSE reader.
                        _ = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                        _ = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                        await TryRefreshAsync(opts, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException ex)
            {
                LogStreamError(logger, ex);
            }
            catch (IOException ex)
            {
                LogStreamError(logger, ex);
            }

            // Reconnect with exponential backoff. Fall back to polling during the gap.
            try
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, s_maxBackoff.TotalMilliseconds));
            await TryRefreshAsync(opts, ct).ConfigureAwait(false);
        }
    }

    private async Task TryRefreshAsync(FeatlySdkOptions opts, CancellationToken ct)
    {
        try
        {
            var current = cache.Current;
            var result = await http.FetchConfigAsync(opts.EnvironmentKey, current.Etag, ct).ConfigureAwait(false);

            if (result.NotModified)
            {
                LogNotModified(logger);
                return;
            }

            if (result.Snapshot is not null)
            {
                cache.Replace(result.Snapshot, result.Etag);
                LogSnapshotUpdated(logger, result.Snapshot.Flags.Count, result.Etag ?? "<none>");
                await FeatlySnapshotFileStore.TryPersistCacheAsync(opts.OfflineCachePath, result.Snapshot, result.Etag, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            LogRefreshError(logger, ex);
        }
        catch (TaskCanceledException ex)
        {
            LogRefreshError(logger, ex);
        }
    }

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information, Message = "Featly cache seeded from {Source}: {FlagCount} flag(s).")]
    private static partial void LogSeeded(ILogger logger, string source, int flagCount);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Featly config not modified.")]
    private static partial void LogNotModified(ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Featly snapshot updated: {FlagCount} flag(s), etag={Etag}.")]
    private static partial void LogSnapshotUpdated(ILogger logger, int flagCount, string etag);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Featly config refresh failed.")]
    private static partial void LogRefreshError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Featly SSE stream connected.")]
    private static partial void LogStreamConnected(ILogger logger);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "Featly SSE stream error; reconnecting.")]
    private static partial void LogStreamError(ILogger logger, Exception exception);
}
