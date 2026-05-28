using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryWebhookStore : IWebhookStore
{
    private readonly ConcurrentDictionary<Guid, WebhookEndpoint> _byId = new();

    public Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var e) ? e : null);

    public Task<IReadOnlyList<WebhookEndpoint>> ListAsync(CancellationToken ct)
    {
        var list = _byId.Values.OrderByDescending(e => e.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<WebhookEndpoint>>(list);
    }

    public Task UpsertAsync(WebhookEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;
        _byId[endpoint.Id] = endpoint;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        _byId.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
