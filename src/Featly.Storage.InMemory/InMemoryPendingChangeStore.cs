using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryPendingChangeStore : IPendingChangeStore
{
    private readonly ConcurrentDictionary<Guid, PendingChange> _byId = new();

    public Task<PendingChange?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var c) ? c : null);

    public Task<IReadOnlyList<PendingChange>> ListAsync(CancellationToken ct)
    {
        var list = _byId.Values.OrderByDescending(c => c.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<PendingChange>>(list);
    }

    public Task<IReadOnlyList<PendingChange>> ListByStatusAsync(ChangeStatus status, CancellationToken ct)
    {
        var list = _byId.Values.Where(c => c.Status == status).OrderByDescending(c => c.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<PendingChange>>(list);
    }

    public Task<IReadOnlyList<PendingChange>> ListByEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        var list = _byId.Values.Where(c => c.EnvironmentId == environmentId).OrderByDescending(c => c.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<PendingChange>>(list);
    }

    public Task<IReadOnlyList<PendingChange>> ListOpenForEntityAsync(string entityType, string entityKey, Guid environmentId, CancellationToken ct)
    {
        var list = _byId.Values
            .Where(c => c.EnvironmentId == environmentId
                && string.Equals(c.EntityType, entityType, StringComparison.Ordinal)
                && string.Equals(c.EntityKey, entityKey, StringComparison.Ordinal)
                && (c.Status == ChangeStatus.Pending || c.Status == ChangeStatus.Approved))
            .OrderByDescending(c => c.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<PendingChange>>(list);
    }

    public Task CreateAsync(PendingChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!_byId.TryAdd(change.Id, change))
        {
            throw new InvalidOperationException($"PendingChange '{change.Id}' already exists.");
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PendingChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        _byId.AddOrUpdate(change.Id, change, (_, _) => change);
        return Task.CompletedTask;
    }
}
