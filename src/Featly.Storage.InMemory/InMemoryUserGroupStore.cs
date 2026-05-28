using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryUserGroupStore : IUserGroupStore
{
    private readonly ConcurrentDictionary<string, UserGroup> _byKey = new(StringComparer.Ordinal);

    public Task<UserGroup?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_byKey.TryGetValue(key, out var g) ? g : null);
    }

    public Task<UserGroup?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_byKey.Values.FirstOrDefault(g => g.Id == id));

    public Task<IReadOnlyList<UserGroup>> ListAsync(CancellationToken ct)
    {
        var list = _byKey.Values.OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        return Task.FromResult<IReadOnlyList<UserGroup>>(list);
    }

    public Task<IReadOnlyList<UserGroup>> ListForMemberAsync(Guid userId, CancellationToken ct)
    {
        var list = _byKey.Values.Where(g => g.MemberUserIds.Contains(userId)).ToList();
        return Task.FromResult<IReadOnlyList<UserGroup>>(list);
    }

    public Task UpsertAsync(UserGroup group, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.UpdatedAt = DateTimeOffset.UtcNow;

        _byKey.AddOrUpdate(
            group.Key,
            _ => group,
            (_, existing) =>
            {
                existing.Name = group.Name;
                existing.Description = group.Description;
                existing.MemberUserIds = [.. group.MemberUserIds];
                existing.UpdatedAt = group.UpdatedAt;
                return existing;
            });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _byKey.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
