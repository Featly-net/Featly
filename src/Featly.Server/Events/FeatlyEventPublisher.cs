using Microsoft.Extensions.Logging;

namespace Featly.Server.Events;

/// <summary>
/// Default fan-out publisher: invokes every registered consumer in turn,
/// isolating failures so one bad consumer never breaks the mutation that
/// produced the event (telemetry is best-effort).
/// </summary>
internal sealed partial class FeatlyEventPublisher(
    IEnumerable<IFeatlyEventConsumer> consumers,
    ILogger<FeatlyEventPublisher> logger) : IFeatlyEventPublisher
{
    private readonly IFeatlyEventConsumer[] _consumers = [.. consumers];

    public async ValueTask PublishAsync(FeatlyDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        foreach (var consumer in _consumers)
        {
            try
            {
                await consumer.HandleAsync(domainEvent, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Consumers are best-effort; never let one break the request.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogConsumerFailed(logger, consumer.GetType().Name, domainEvent.Type, ex);
            }
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error,
        Message = "Featly event consumer {Consumer} failed handling {EventType}.")]
    private static partial void LogConsumerFailed(ILogger logger, string consumer, string eventType, Exception exception);
}
