using System.Security.Claims;
using System.Text.Json;
using Featly.Server.Approval;

namespace Featly.Server.Events;

/// <summary>
/// Publishes a <see cref="FeatlyDomainEvent"/> to every registered
/// <see cref="IFeatlyEventConsumer"/> (ARCHITECTURE.md §17). Producers — the
/// admin mutation endpoints, the approval apply path, and RBAC changes — call
/// this after a successful action. The audit recorder and (in 10C) the webhook
/// dispatcher are the consumers. Publishing must never throw into the request
/// path: a misbehaving consumer is logged and skipped.
/// </summary>
public interface IFeatlyEventPublisher
{
    /// <summary>Fans the event out to all consumers.</summary>
    ValueTask PublishAsync(FeatlyDomainEvent domainEvent, CancellationToken ct);
}

/// <summary>A sink for <see cref="FeatlyDomainEvent"/>s (audit log, webhook dispatch).</summary>
public interface IFeatlyEventConsumer
{
    /// <summary>Handles one event. Should be cheap and must not assume ordering.</summary>
    ValueTask HandleAsync(FeatlyDomainEvent domainEvent, CancellationToken ct);
}

/// <summary>
/// Convenience helpers so endpoints publish in a single call, resolving the
/// actor from the request principal and serializing an optional payload.
/// </summary>
internal static class FeatlyEventPublisherExtensions
{
    public static ValueTask PublishAsync(
        this IFeatlyEventPublisher publisher,
        string type,
        string entityType,
        string? entityKey,
        Guid? environmentId,
        ClaimsPrincipal user,
        object? data,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        var name = user.Identity?.Name;
        return publisher.PublishAsync(new FeatlyDomainEvent
        {
            Type = type,
            EntityType = entityType,
            EntityKey = entityKey,
            EnvironmentId = environmentId,
            ActorIdentifier = string.IsNullOrEmpty(name) ? null : name,
            Data = data is null ? null : JsonSerializer.SerializeToElement(data, ChangeJson.Options),
            At = DateTimeOffset.UtcNow,
        }, ct);
    }
}
