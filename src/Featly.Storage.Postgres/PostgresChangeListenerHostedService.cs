using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Featly.Storage.Postgres;

/// <summary>
/// Keeps a persistent <c>LISTEN</c> connection open on <see cref="PostgresChangeNotifier.Channel"/>
/// and delivers every notification it receives to <see cref="PostgresChangeNotifier.DispatchLocallyAsync"/>
/// (ADR-0026, issue #258) — the piece that turns the in-process fan-out every
/// provider has into a cross-replica one for Postgres.
/// </summary>
/// <remarks>
/// A dedicated raw <see cref="NpgsqlConnection"/>, not a pooled
/// <c>DbContext</c>: this connection sits open and idle between notifications
/// for as long as the host runs, which is the opposite of what the pooled
/// factory the rest of the provider uses is for. If the connection drops (a
/// network blip, the database restarting) the loop reconnects and re-issues
/// <c>LISTEN</c> with the same exponential backoff (1s doubling to 30s) the
/// SDK's config sync service uses for its own reconnects — while disconnected,
/// clients still catch up on their next poll, so a gap here is degraded
/// freshness, not a correctness failure.
/// </remarks>
internal sealed partial class PostgresChangeListenerHostedService(
    IOptions<PostgresFeatlyStoreOptions> options,
    PostgresChangeNotifier notifier,
    ILogger<PostgresChangeListenerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan s_minBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_maxBackoff = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes once <c>LISTEN</c> has been issued and this instance is
    /// actively receiving notifications. <see cref="BackgroundService.StartAsync"/>
    /// returns as soon as the background loop is scheduled, not once it has
    /// actually connected, so a caller that needs to know delivery is live
    /// (tests simulating multiple replicas; a future health check) awaits this
    /// instead of racing the connection.
    /// </summary>
    internal Task ListeningAsync => _listening.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = s_minBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Bounded and single-writer: the Notification event (below) is the
            // only producer, this loop the only consumer, so a slow consumer
            // drops the oldest pending notification rather than growing without
            // bound — SSE clients that missed one still catch up on their next
            // poll, same trade-off SdkEndpoints.StreamAsync already makes.
            var pending = Channel.CreateBounded<ChangeNotification>(new BoundedChannelOptions(capacity: 256)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });

            try
            {
                await using var connection = new NpgsqlConnection(options.Value.ConnectionString);
                connection.Notification += (_, e) => OnNotification(e, pending.Writer);
                await connection.OpenAsync(stoppingToken).ConfigureAwait(false);

                await using (var listen = new NpgsqlCommand($"LISTEN {PostgresChangeNotifier.Channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
                }
                LogListening(logger);
                backoff = s_minBackoff;
                _listening.TrySetResult();

                var consume = ConsumeAsync(pending.Reader, stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Blocks until a notification arrives; the event handler
                    // above (fired from inside this call) queues it for the
                    // consume loop and this immediately waits for the next one.
                    await connection.WaitAsync(stoppingToken).ConfigureAwait(false);
                }

                pending.Writer.TryComplete();
                await consume.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The listen loop must survive any single failure and retry.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogListenFailed(logger, ex);
                try
                {
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, s_maxBackoff.TotalMilliseconds));
            }
        }
    }

    private void OnNotification(NpgsqlNotificationEventArgs e, ChannelWriter<ChangeNotification> writer)
    {
        ChangeNotification? notification;
        try
        {
            notification = JsonSerializer.Deserialize<ChangeNotification>(e.Payload);
        }
        catch (JsonException ex)
        {
            // A malformed payload must not take down the listener -- log and
            // move on, same failure-isolation policy InProcessChangeNotifier
            // applies to a misbehaving subscriber.
            LogMalformedPayload(logger, ex);
            return;
        }

        if (notification is not null)
        {
            writer.TryWrite(notification);
        }
    }

    private async Task ConsumeAsync(ChannelReader<ChangeNotification> reader, CancellationToken ct)
    {
        await foreach (var notification in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await notifier.DispatchLocallyAsync(notification, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information,
        Message = "Listening for Featly change notifications on PostgreSQL channel '" + PostgresChangeNotifier.Channel + "'.")]
    private static partial void LogListening(ILogger logger);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Warning,
        Message = "Featly change listener lost its PostgreSQL connection; reconnecting.")]
    private static partial void LogListenFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Warning,
        Message = "Discarding a malformed change notification payload.")]
    private static partial void LogMalformedPayload(ILogger logger, Exception exception);
}
