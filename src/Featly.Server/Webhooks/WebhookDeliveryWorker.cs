using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Webhooks;

/// <summary>
/// Drains the persisted webhook delivery queue (ARCHITECTURE.md §17). Each scan
/// claims due <see cref="WebhookDelivery"/> rows, signs the payload with the
/// target endpoint's secret, POSTs it, and writes the outcome back: a 2xx marks
/// the delivery <see cref="WebhookDeliveryStatus.Succeeded"/>; anything else
/// reschedules with exponential backoff until the attempt budget runs out and
/// the row is dead-lettered.
/// </summary>
internal sealed partial class WebhookDeliveryWorker(
    IHttpClientFactory httpClientFactory,
    StorageFacade store,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDeliveryWorker> logger) : BackgroundService
{
    /// <summary>Named <see cref="HttpClient"/> for outbound deliveries.</summary>
    public const string HttpClientName = "Featly.Webhooks";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(opts.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await DrainOnceAsync(opts, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The worker must survive any single-scan failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogScanFailed(logger, ex);
            }
        }
    }

    private async Task DrainOnceAsync(WebhookOptions opts, CancellationToken ct)
    {
        var due = await store.WebhookDeliveries.ListDueAsync(DateTimeOffset.UtcNow, opts.BatchSize, ct).ConfigureAwait(false);
        foreach (var delivery in due)
        {
            await AttemptAsync(delivery, opts, ct).ConfigureAwait(false);
        }
    }

    private async Task AttemptAsync(WebhookDelivery delivery, WebhookOptions opts, CancellationToken ct)
    {
        var endpoint = await store.Webhooks.GetByIdAsync(delivery.WebhookEndpointId, ct).ConfigureAwait(false);
        if (endpoint is null || !endpoint.Enabled)
        {
            // Target removed or disabled since enqueue — abandon the delivery.
            delivery.Status = WebhookDeliveryStatus.Dead;
            delivery.LastError = endpoint is null ? "Endpoint no longer exists." : "Endpoint is disabled.";
            await store.WebhookDeliveries.UpdateAsync(delivery, ct).ConfigureAwait(false);
            return;
        }

        delivery.AttemptCount++;
        int? statusCode = null;
        string? error = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(WebhookSignature.Header, WebhookSignature.Compute(endpoint.Secret, delivery.Payload));
            request.Headers.TryAddWithoutValidation("X-Featly-Event", delivery.EventType);
            request.Headers.TryAddWithoutValidation("X-Featly-Delivery", delivery.Id.ToString());

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(opts.RequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                error = $"Endpoint returned {statusCode}.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // host shutting down — leave the row Pending to retry next boot.
        }
        catch (OperationCanceledException)
        {
            error = "Request timed out.";
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }

        var now = DateTimeOffset.UtcNow;
        delivery.LastStatusCode = statusCode;
        delivery.UpdatedAt = now;

        if (error is null)
        {
            delivery.Status = WebhookDeliveryStatus.Succeeded;
            delivery.LastError = null;
            delivery.DeliveredAt = now;
            LogDelivered(logger, delivery.Id, endpoint.Url, statusCode ?? 0);
        }
        else if (delivery.AttemptCount >= opts.MaxAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Dead;
            delivery.LastError = error;
            LogDead(logger, delivery.Id, endpoint.Url, delivery.AttemptCount, error);
        }
        else
        {
            delivery.Status = WebhookDeliveryStatus.Pending;
            delivery.LastError = error;
            delivery.NextAttemptAt = now + Backoff(delivery.AttemptCount, opts);
            LogRetry(logger, delivery.Id, endpoint.Url, delivery.AttemptCount, error);
        }

        await store.WebhookDeliveries.UpdateAsync(delivery, ct).ConfigureAwait(false);
    }

    /// <summary>Exponential backoff: base * 2^(attempt-1), capped at the configured maximum.</summary>
    internal static TimeSpan Backoff(int attemptCount, WebhookOptions opts)
    {
        var factor = Math.Pow(2, Math.Max(0, attemptCount - 1));
        var ms = opts.BaseRetryDelay.TotalMilliseconds * factor;
        var cappedMs = Math.Min(ms, opts.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(cappedMs);
    }

    [LoggerMessage(EventId = 4101, Level = LogLevel.Debug, Message = "Webhook delivery {DeliveryId} to {Url} succeeded ({StatusCode}).")]
    private static partial void LogDelivered(ILogger logger, Guid deliveryId, string url, int statusCode);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Warning, Message = "Webhook delivery {DeliveryId} to {Url} failed (attempt {Attempt}); will retry. {Error}")]
    private static partial void LogRetry(ILogger logger, Guid deliveryId, string url, int attempt, string error);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Error, Message = "Webhook delivery {DeliveryId} to {Url} dead-lettered after {Attempt} attempts. {Error}")]
    private static partial void LogDead(ILogger logger, Guid deliveryId, string url, int attempt, string error);

    [LoggerMessage(EventId = 4104, Level = LogLevel.Error, Message = "Webhook delivery scan failed.")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);
}
