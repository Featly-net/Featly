using System.Net.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Featly.Sdk.Internal;

/// <summary>
/// Drains the <see cref="ChannelEventSink"/> and uploads telemetry in batches to
/// <c>POST /api/sdk/events</c>. Flushes when a batch fills or a short timer
/// elapses, so events leave promptly without a request per event. On shutdown it
/// makes a best-effort final flush of whatever is buffered. Upload failures are
/// logged and the batch is dropped — telemetry is best-effort and must never
/// crash the host.
/// </summary>
internal sealed partial class FeatlyEventFlushService(
    ChannelEventSink sink,
    FeatlyHttpClient http,
    IOptions<FeatlySdkOptions> options,
    ILogger<FeatlyEventFlushService> logger)
    : BackgroundService
{
    private const int MaxBatchSize = 200;
    private static readonly TimeSpan s_flushInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var reader = sink.Reader;
        var batch = new List<QueuedEvent>(MaxBatchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                // Drain what's immediately available, then wait briefly to let a
                // burst coalesce before flushing.
                DrainAvailable(reader, batch);
                if (batch.Count == 0)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(s_flushInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down — flush what we have below before exiting.
                }

                DrainAvailable(reader, batch);
                await FlushAsync(opts, batch, CancellationToken.None).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }

        // Final best-effort drain + flush so buffered events aren't lost.
        DrainAvailable(reader, batch);
        if (batch.Count > 0)
        {
            await FlushAsync(opts, batch, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void DrainAvailable(System.Threading.Channels.ChannelReader<QueuedEvent> reader, List<QueuedEvent> batch)
    {
        while (batch.Count < MaxBatchSize && reader.TryRead(out var evt))
        {
            batch.Add(evt);
        }
    }

    private async Task FlushAsync(FeatlySdkOptions opts, List<QueuedEvent> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await http.SendEventsAsync(opts.EnvironmentKey, batch, ct).ConfigureAwait(false);
            LogFlushed(logger, batch.Count);
        }
        catch (HttpRequestException ex)
        {
            LogFlushError(logger, batch.Count, ex);
        }
        catch (TaskCanceledException ex)
        {
            LogFlushError(logger, batch.Count, ex);
        }
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Debug, Message = "Featly flushed {Count} event(s).")]
    private static partial void LogFlushed(ILogger logger, int count);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "Featly event flush failed; dropped {Count} event(s).")]
    private static partial void LogFlushError(ILogger logger, int count, Exception exception);
}
