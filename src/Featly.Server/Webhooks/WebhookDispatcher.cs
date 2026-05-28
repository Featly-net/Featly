using System.Text.Json;
using Featly.Server.Events;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Webhooks;

/// <summary>
/// Event consumer that fans a <see cref="FeatlyDomainEvent"/> out to the webhook
/// queue: for each enabled endpoint whose subscription matches the event type
/// and environment, it enqueues a <see cref="WebhookDelivery"/> carrying the
/// serialized payload. The background worker signs and POSTs it later.
/// </summary>
internal sealed class WebhookDispatcher(StorageFacade store) : IFeatlyEventConsumer
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async ValueTask HandleAsync(FeatlyDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var endpoints = await store.Webhooks.ListAsync(ct).ConfigureAwait(false);
        var matching = endpoints.Where(e => Matches(e, domainEvent)).ToList();
        if (matching.Count == 0)
        {
            return;
        }

        var payload = BuildPayload(domainEvent);
        var now = DateTimeOffset.UtcNow;
        var deliveries = matching.Select(endpoint => new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookEndpointId = endpoint.Id,
            EventType = domainEvent.Type,
            Payload = payload,
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();

        await store.WebhookDeliveries.EnqueueAsync(deliveries, ct).ConfigureAwait(false);
    }

    /// <summary>Serializes a domain event into the JSON body delivered to endpoints.</summary>
    public static string BuildPayload(FeatlyDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return JsonSerializer.Serialize(new WebhookPayload(
            domainEvent.Type,
            domainEvent.EntityType,
            domainEvent.EntityKey,
            domainEvent.EnvironmentId,
            domainEvent.ActorIdentifier,
            domainEvent.At,
            domainEvent.Data), s_json);
    }

    /// <summary>An endpoint receives an event when enabled and the type + env filters match.</summary>
    public static bool Matches(WebhookEndpoint endpoint, FeatlyDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (!endpoint.Enabled)
        {
            return false;
        }

        // Empty subscription = all event types.
        if (endpoint.EventTypes.Count > 0 && !endpoint.EventTypes.Contains(domainEvent.Type, StringComparer.Ordinal))
        {
            return false;
        }

        // Null env filter = all environments. Events without an environment
        // (e.g. RBAC unassign) only reach endpoints with no env filter.
        if (endpoint.EnvironmentId is { } envFilter && endpoint.EnvironmentId != domainEvent.EnvironmentId)
        {
            _ = envFilter;
            return false;
        }

        return true;
    }
}

/// <summary>The JSON envelope POSTed to webhook endpoints.</summary>
internal sealed record WebhookPayload(
    string Type,
    string EntityType,
    string? EntityKey,
    Guid? EnvironmentId,
    string? Actor,
    DateTimeOffset At,
    JsonElement? Data);
