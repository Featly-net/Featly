using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Featly.Server.Settings;
using Featly.Server.Telemetry;
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
    IFeatlySettingsProvider settings,
    FeatlyServerMetrics metrics,
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

    /// <summary>
    /// Safety margin added on top of a single request timeout when leasing a row,
    /// so the lease reliably outlasts one delivery attempt (issue #237).
    /// </summary>
    private static readonly TimeSpan LeaseMargin = TimeSpan.FromSeconds(30);

    private async Task DrainOnceAsync(WebhookOptions opts, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await store.WebhookDeliveries.ListDueAsync(now, opts.BatchSize, ct).ConfigureAwait(false);
        foreach (var delivery in due)
        {
            // Multi-instance safety (issue #237): lease the row before attempting
            // it so another instance draining the same queue treats it as not-yet-
            // due and skips it. AttemptAsync's write below overwrites the lease with
            // the real outcome; the loser of the race just moves on.
            var leaseUntil = DateTimeOffset.UtcNow + opts.RequestTimeout + LeaseMargin;
            if (!await store.WebhookDeliveries.TryClaimDueAsync(delivery.Id, now, leaseUntil, ct).ConfigureAwait(false))
            {
                continue;
            }
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

        var now = DateTimeOffset.UtcNow;

        // Retry + circuit-breaker tuning is DB-overridable (ARCHITECTURE.md §15):
        // the effective values come from the settings provider (DB beats
        // appsettings), not directly from WebhookOptions.
        var tuning = settings.Webhook;

        // Circuit breaker (issue #207): while this endpoint's circuit is open, skip
        // the POST entirely and defer the delivery to the half-open probe time, so
        // a consistently-failing endpoint can't clog the queue. A short-circuited
        // delivery does not spend the attempt budget.
        if (tuning.CircuitBreakerThreshold > 0 && endpoint.CircuitOpenUntil is { } openUntil && openUntil > now)
        {
            delivery.NextAttemptAt = openUntil;
            delivery.UpdatedAt = now;
            await store.WebhookDeliveries.UpdateAsync(delivery, ct).ConfigureAwait(false);
            LogCircuitOpen(logger, delivery.Id, endpoint.Url, openUntil);
            return;
        }

        // SSRF guard at delivery time (issue #189): re-resolve the target and
        // refuse internal ranges. This also defeats DNS rebinding, where a host
        // that looked public at create time now resolves to a private address.
        if (!await IsTargetAllowedAsync(endpoint.Url, opts, ct).ConfigureAwait(false))
        {
            delivery.Status = WebhookDeliveryStatus.Dead;
            delivery.LastError = "Target resolves to a blocked (internal) address range.";
            delivery.UpdatedAt = DateTimeOffset.UtcNow;
            await store.WebhookDeliveries.UpdateAsync(delivery, ct).ConfigureAwait(false);
            LogBlockedTarget(logger, delivery.Id, endpoint.Url);
            return;
        }

        delivery.AttemptCount++;
        int? statusCode = null;
        string? error = null;

        using var activity = metrics.ActivitySource.StartActivity("featly.webhook.deliver");
        activity?.SetTag("featly.webhook.id", endpoint.Id);
        activity?.SetTag("featly.event_type", delivery.EventType);
        activity?.SetTag("featly.webhook.attempt", delivery.AttemptCount);

        var startedAt = Stopwatch.GetTimestamp();
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

        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        metrics.RecordWebhookDelivery(success: error is null, elapsedMs);
        if (statusCode is not null)
        {
            activity?.SetTag("featly.webhook.status_code", statusCode);
        }
        activity?.SetStatus(error is null ? ActivityStatusCode.Ok : ActivityStatusCode.Error, error);

        now = DateTimeOffset.UtcNow;
        delivery.LastStatusCode = statusCode;
        delivery.UpdatedAt = now;

        if (error is null)
        {
            delivery.Status = WebhookDeliveryStatus.Succeeded;
            delivery.LastError = null;
            delivery.DeliveredAt = now;
            LogDelivered(logger, delivery.Id, endpoint.Url, statusCode ?? 0);
        }
        else if (delivery.AttemptCount >= tuning.MaxAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Dead;
            delivery.LastError = error;
            LogDead(logger, delivery.Id, endpoint.Url, delivery.AttemptCount, error);
        }
        else
        {
            delivery.Status = WebhookDeliveryStatus.Pending;
            delivery.LastError = error;
            delivery.NextAttemptAt = now + Backoff(
                delivery.AttemptCount,
                TimeSpan.FromSeconds(tuning.BaseRetryDelaySeconds),
                TimeSpan.FromSeconds(tuning.MaxRetryDelaySeconds));
            LogRetry(logger, delivery.Id, endpoint.Url, delivery.AttemptCount, error);
        }

        await store.WebhookDeliveries.UpdateAsync(delivery, ct).ConfigureAwait(false);
        await RecordCircuitOutcomeAsync(endpoint, success: error is null, tuning, now, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the endpoint's circuit-breaker state after a delivery attempt
    /// (issue #207): a success closes the circuit and clears the failure streak; a
    /// failure increments it and opens the circuit once the threshold is reached.
    /// No-op when the breaker is disabled (threshold &lt;= 0).
    /// </summary>
    private async Task RecordCircuitOutcomeAsync(WebhookEndpoint endpoint, bool success, FeatlyWebhookSettings tuning, DateTimeOffset now, CancellationToken ct)
    {
        if (tuning.CircuitBreakerThreshold <= 0)
        {
            return;
        }

        if (success)
        {
            if (endpoint.ConsecutiveFailures > 0 || endpoint.CircuitOpenUntil is not null)
            {
                await store.Webhooks.RecordCircuitStateAsync(endpoint.Id, 0, null, ct).ConfigureAwait(false);
                LogCircuitClosed(logger, endpoint.Id, endpoint.Url);
            }
            return;
        }

        var failures = endpoint.ConsecutiveFailures + 1;
        var openUntil = endpoint.CircuitOpenUntil;
        if (failures >= tuning.CircuitBreakerThreshold)
        {
            var reopen = now + TimeSpan.FromSeconds(tuning.CircuitBreakerCooldownSeconds);
            openUntil = reopen;
            LogCircuitOpened(logger, endpoint.Id, endpoint.Url, failures, reopen);
        }

        await store.Webhooks.RecordCircuitStateAsync(endpoint.Id, failures, openUntil, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Delivery-time SSRF check: <c>true</c> when the target may be POSTed to.
    /// Skipped when the operator has opted into private targets; otherwise the
    /// URL must parse and resolve outside the blocked ranges (issue #189).
    /// </summary>
    internal static async Task<bool> IsTargetAllowedAsync(string url, WebhookOptions opts, CancellationToken ct)
    {
        if (opts.AllowPrivateNetworkTargets)
        {
            return true;
        }
        return Uri.TryCreate(url, UriKind.Absolute, out var target)
            && await WebhookTargetGuard.IsAllowedAtDeliveryAsync(target, ct).ConfigureAwait(false);
    }

    /// <summary>Exponential backoff: base * 2^(attempt-1), capped at the configured maximum.</summary>
    internal static TimeSpan Backoff(int attemptCount, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var factor = Math.Pow(2, Math.Max(0, attemptCount - 1));
        var ms = baseDelay.TotalMilliseconds * factor;
        var cappedMs = Math.Min(ms, maxDelay.TotalMilliseconds);
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

    [LoggerMessage(EventId = 4105, Level = LogLevel.Warning, Message = "Webhook delivery {DeliveryId} to {Url} blocked: target resolves to an internal address range.")]
    private static partial void LogBlockedTarget(ILogger logger, Guid deliveryId, string url);

    [LoggerMessage(EventId = 4106, Level = LogLevel.Debug, Message = "Webhook delivery {DeliveryId} to {Url} short-circuited: endpoint circuit is open until {OpenUntil:o}.")]
    private static partial void LogCircuitOpen(ILogger logger, Guid deliveryId, string url, DateTimeOffset openUntil);

    [LoggerMessage(EventId = 4107, Level = LogLevel.Warning, Message = "Webhook endpoint {EndpointId} ({Url}) circuit opened after {Failures} consecutive failures; suppressing deliveries until {OpenUntil:o}.")]
    private static partial void LogCircuitOpened(ILogger logger, Guid endpointId, string url, int failures, DateTimeOffset openUntil);

    [LoggerMessage(EventId = 4108, Level = LogLevel.Information, Message = "Webhook endpoint {EndpointId} ({Url}) circuit closed after a successful delivery.")]
    private static partial void LogCircuitClosed(ILogger logger, Guid endpointId, string url);
}
