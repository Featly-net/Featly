using System.Text.Json;
using Featly.Server.Events;
using Featly.Server.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Approval;

/// <summary>
/// Drains <see cref="PendingChange"/> rows whose <see cref="PendingChange.ScheduledApplyAt"/>
/// is due (ADR-0028), shaped like <see cref="Webhooks.WebhookDeliveryWorker"/>. Each
/// scan claims <c>Approved</c> changes past their schedule and drives them through
/// the exact same <see cref="ChangeApplicationService"/> + <see cref="ChangeStaleness"/>
/// path manual Apply uses — a change that went stale since approval is skipped,
/// never forced through.
/// </summary>
/// <remarks>
/// There is no HTTP request behind a scheduled apply, so there is no
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> to resolve an actor
/// from. The worker publishes <see cref="FeatlyDomainEvent"/>s directly with a
/// fixed <see cref="ActorIdentifier"/> and leaves <see cref="PendingChange.AppliedByUserId"/>
/// <c>null</c> — the same way a non-human actor (e.g. an expired-key sweep)
/// would be recorded.
/// </remarks>
internal sealed partial class ScheduledApplyWorker(
    StorageFacade store,
    ChangeApplicationService applier,
    IFeatlyEventPublisher events,
    FeatlyServerMetrics metrics,
    IOptions<ScheduledApplyOptions> options,
    ILogger<ScheduledApplyWorker> logger) : BackgroundService
{
    /// <summary>Recorded as the actor for changes this worker applies or skips.</summary>
    internal const string ActorIdentifier = "scheduled-apply-worker";

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

    /// <summary>Runs a single scan on demand — used by tests to avoid waiting on <see cref="ScheduledApplyOptions.PollInterval"/>.</summary>
    internal Task RunOnceAsync(CancellationToken ct) => DrainOnceAsync(options.Value, ct);

    private async Task DrainOnceAsync(ScheduledApplyOptions opts, CancellationToken ct)
    {
        var approved = await store.PendingChanges.ListByStatusAsync(ChangeStatus.Approved, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var claimed = 0;
        foreach (var change in approved)
        {
            if (change.ScheduledApplyAt is not { } scheduledAt || scheduledAt > now)
            {
                continue;
            }

            await ApplyDueAsync(change, ct).ConfigureAwait(false);

            claimed++;
            if (claimed >= opts.BatchSize)
            {
                return;
            }
        }
    }

    private async Task ApplyDueAsync(PendingChange change, CancellationToken ct)
    {
        if (await ChangeStaleness.IsStaleAsync(store, change, ct).ConfigureAwait(false))
        {
            change.Status = ChangeStatus.Stale;
            change.UpdatedAt = DateTimeOffset.UtcNow;
            await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);
            LogSkippedStale(logger, change.Id, change.EntityType, change.EntityKey);

            await events.PublishAsync(new FeatlyDomainEvent
            {
                Type = FeatlyEventTypes.ChangeScheduleSkippedStale,
                EntityType = change.EntityType,
                EntityKey = change.EntityKey,
                EnvironmentId = change.EnvironmentId,
                ActorIdentifier = ActorIdentifier,
                Data = JsonSerializer.SerializeToElement(new { change.Id, change.EntityType, change.EntityKey, change.Action }, ChangeJson.Options),
            }, ct).ConfigureAwait(false);
            return;
        }

        var applied = await applier.ApplyAsync(change, ActorIdentifier, ct).ConfigureAwait(false);
        if (!applied)
        {
            LogApplyFailed(logger, change.Id, change.EntityType);
            return;
        }

        change.Status = ChangeStatus.Applied;
        change.AppliedByUserId = null;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.UpdatedAt = DateTimeOffset.UtcNow;
        await store.PendingChanges.UpdateAsync(change, ct).ConfigureAwait(false);
        await ChangeStaleness.MarkSiblingsStaleAsync(store, change, ct).ConfigureAwait(false);

        metrics.RecordChangeApplied(change.Action, bypassed: false);
        LogApplied(logger, change.Id, change.EntityType, change.EntityKey);

        await events.PublishAsync(new FeatlyDomainEvent
        {
            Type = FeatlyEventTypes.ChangeApplied,
            EntityType = change.EntityType,
            EntityKey = change.EntityKey,
            EnvironmentId = change.EnvironmentId,
            ActorIdentifier = ActorIdentifier,
            Data = JsonSerializer.SerializeToElement(new { change.Id, change.EntityType, change.EntityKey, change.Action, emergency = false }, ChangeJson.Options),
        }, ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information, Message = "Scheduled apply {ChangeId} ({EntityType}/{EntityKey}) applied.")]
    private static partial void LogApplied(ILogger logger, Guid changeId, string entityType, string entityKey);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Warning, Message = "Scheduled apply {ChangeId} ({EntityType}/{EntityKey}) skipped: change went stale since approval.")]
    private static partial void LogSkippedStale(ILogger logger, Guid changeId, string entityType, string entityKey);

    [LoggerMessage(EventId = 3403, Level = LogLevel.Error, Message = "Scheduled apply {ChangeId} failed: unsupported entity type {EntityType}.")]
    private static partial void LogApplyFailed(ILogger logger, Guid changeId, string entityType);

    [LoggerMessage(EventId = 3404, Level = LogLevel.Error, Message = "Scheduled apply scan failed.")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);
}
