using Featly.Server.Telemetry;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Events;

/// <summary>
/// Event consumer that persists each <see cref="FeatlyDomainEvent"/> as an
/// immutable <see cref="AuditEntry"/> — the audit-log half of M10.
/// </summary>
internal sealed class AuditRecorder(StorageFacade store, FeatlyServerMetrics metrics) : IFeatlyEventConsumer
{
    public async ValueTask HandleAsync(FeatlyDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await store.Audit.AppendAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = domainEvent.At,
            Action = domainEvent.Type,
            EntityType = domainEvent.EntityType,
            EntityKey = domainEvent.EntityKey,
            EnvironmentId = domainEvent.EnvironmentId,
            ActorIdentifier = domainEvent.ActorIdentifier,
            Data = domainEvent.Data,
        }, ct).ConfigureAwait(false);

        metrics.RecordAuditWrite(domainEvent.Type);
    }
}
