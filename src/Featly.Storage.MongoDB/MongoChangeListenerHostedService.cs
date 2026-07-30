using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Featly.Storage.MongoDB;

/// <summary>
/// Keeps an open Change Stream cursor on <see cref="MongoCollectionNames.ChangeNotifications"/>
/// and delivers every insert it observes to <see cref="MongoChangeNotifier.DispatchLocallyAsync"/>
/// (ADR-0034) — the piece that turns the in-process fan-out every provider
/// has into a cross-replica one for MongoDB.
/// </summary>
/// <remarks>
/// No resume-token persistence: on a dropped cursor (a network blip, the
/// replica set stepping down) the loop reconnects and opens a fresh Watch()
/// with the same exponential backoff (1s doubling to 30s)
/// <c>PostgresChangeListenerHostedService</c> uses for its own reconnects —
/// while disconnected, clients still catch up on their next poll, so a gap
/// here is degraded freshness, not a correctness failure. Same trade-off the
/// Postgres provider already accepts.
/// </remarks>
internal sealed partial class MongoChangeListenerHostedService(
    MongoFeatlyDatabase database,
    MongoChangeNotifier notifier,
    ILogger<MongoChangeListenerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan s_minBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_maxBackoff = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes once the Change Stream cursor is open and this instance is
    /// actively receiving notifications. <see cref="BackgroundService.StartAsync"/>
    /// returns as soon as the background loop is scheduled, not once it has
    /// actually opened the cursor, so a caller that needs to know delivery is
    /// live (tests simulating multiple replicas; a future health check) awaits
    /// this instead of racing the connection.
    /// </summary>
    internal Task ListeningAsync => _listening.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = s_minBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var collection = database.Database.GetCollection<BsonDocument>(MongoCollectionNames.ChangeNotifications);
                using var cursor = await collection.WatchAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
                LogListening(logger);
                backoff = s_minBackoff;
                _listening.TrySetResult();

                while (await cursor.MoveNextAsync(stoppingToken).ConfigureAwait(false))
                {
                    foreach (var change in cursor.Current)
                    {
                        if (change.OperationType != ChangeStreamOperationType.Insert || change.FullDocument is null)
                        {
                            continue;
                        }

                        var notification = TryDecodeFullDocument(change.FullDocument, out var error);
                        if (notification is not null)
                        {
                            await notifier.DispatchLocallyAsync(notification, stoppingToken).ConfigureAwait(false);
                        }
                        else if (error is not null)
                        {
                            // A malformed document must not take down the listener --
                            // log and move on, same failure-isolation policy
                            // InProcessChangeNotifier applies to a misbehaving subscriber.
                            LogMalformedPayload(logger, error);
                        }
                    }
                }
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

    /// <summary>
    /// Decodes a change-notification document back into the
    /// <see cref="ChangeNotification"/> <see cref="MongoChangeNotifier.NotifyAsync"/>
    /// encoded. Pure and side-effect-free (no logging here) so the decode
    /// logic is testable on its own, independent of a live Change Stream.
    /// </summary>
    internal static ChangeNotification? TryDecodeFullDocument(BsonDocument document, out JsonException? error)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.TryGetValue("payload", out var payloadValue) || !payloadValue.IsString)
        {
            error = new JsonException("Change-notification document is missing a string 'payload' field.");
            return null;
        }

        try
        {
            error = null;
            return JsonSerializer.Deserialize<ChangeNotification>(payloadValue.AsString);
        }
        catch (JsonException ex)
        {
            error = ex;
            return null;
        }
    }

    [LoggerMessage(EventId = 3501, Level = LogLevel.Information,
        Message = "Listening for Featly change notifications on the MongoDB change stream.")]
    private static partial void LogListening(ILogger logger);

    [LoggerMessage(EventId = 3502, Level = LogLevel.Warning,
        Message = "Featly change listener lost its MongoDB change stream; reconnecting.")]
    private static partial void LogListenFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3503, Level = LogLevel.Warning,
        Message = "Discarding a malformed change notification document.")]
    private static partial void LogMalformedPayload(ILogger logger, Exception exception);
}
