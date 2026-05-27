using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<Guid, ApiKey> _byId = new();

    public Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var k) ? k : null);

    public Task<IReadOnlyList<ApiKey>> FindCandidatesByPrefixAsync(string prefix, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var matches = _byId.Values
            .Where(k => !k.Revoked && string.Equals(k.Prefix, prefix, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult<IReadOnlyList<ApiKey>>(matches);
    }

    public Task<IReadOnlyList<ApiKey>> ListAsync(Guid environmentId, CancellationToken ct)
    {
        var list = _byId.Values
            .Where(k => k.EnvironmentId == environmentId)
            .OrderByDescending(k => k.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<ApiKey>>(list);
    }

    public Task CreateAsync(ApiKey apiKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        if (!_byId.TryAdd(apiKey.Id, apiKey))
        {
            throw new InvalidOperationException(
                $"An API key with id '{apiKey.Id}' already exists.");
        }
        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid id, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (_byId.TryGetValue(id, out var existing))
        {
            existing.Revoked = true;
        }
        return Task.CompletedTask;
    }

    public Task TouchLastUsedAsync(Guid id, DateTimeOffset at, CancellationToken ct)
    {
        if (_byId.TryGetValue(id, out var existing))
        {
            existing.LastUsedAt = at;
        }
        return Task.CompletedTask;
    }
}
