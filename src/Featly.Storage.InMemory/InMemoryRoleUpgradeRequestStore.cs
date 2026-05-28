using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryRoleUpgradeRequestStore : IRoleUpgradeRequestStore
{
    private readonly ConcurrentDictionary<Guid, RoleUpgradeRequest> _byId = new();

    public Task<RoleUpgradeRequest?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byId.TryGetValue(id, out var r) ? r : null);

    public Task<IReadOnlyList<RoleUpgradeRequest>> ListAsync(CancellationToken ct)
    {
        var list = _byId.Values.OrderByDescending(r => r.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<RoleUpgradeRequest>>(list);
    }

    public Task<IReadOnlyList<RoleUpgradeRequest>> ListByStatusAsync(RoleUpgradeStatus status, CancellationToken ct)
    {
        var list = _byId.Values.Where(r => r.Status == status).OrderByDescending(r => r.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<RoleUpgradeRequest>>(list);
    }

    public Task CreateAsync(RoleUpgradeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_byId.TryAdd(request.Id, request))
        {
            throw new InvalidOperationException($"RoleUpgradeRequest '{request.Id}' already exists.");
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RoleUpgradeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        _byId.AddOrUpdate(request.Id, request, (_, _) => request);
        return Task.CompletedTask;
    }
}
