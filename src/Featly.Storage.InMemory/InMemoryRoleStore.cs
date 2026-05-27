using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryRoleStore : IRoleStore
{
    private readonly ConcurrentDictionary<string, Role> _byKey = new(StringComparer.Ordinal);

    public Task<Role?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(_byKey.TryGetValue(key, out var r) ? r : null);
    }

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        foreach (var r in _byKey.Values)
        {
            if (r.Id == id)
            { return Task.FromResult<Role?>(r); }
        }
        return Task.FromResult<Role?>(null);
    }

    public Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct)
    {
        var list = _byKey.Values
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<Role>>(list);
    }

    public Task UpsertAsync(Role role, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (_byKey.TryGetValue(role.Key, out var existing) && existing.IsSystem)
        {
            throw new InvalidOperationException(
                $"Role '{role.Key}' is a system role and cannot be mutated through UpsertAsync. Use UpsertSystemRoleAsync (seed-only path) instead.");
        }
        if (role.IsSystem)
        {
            throw new InvalidOperationException(
                $"Cannot create a new role with IsSystem=true through UpsertAsync. Use UpsertSystemRoleAsync for the seed path.");
        }

        role.UpdatedAt = DateTimeOffset.UtcNow;

        _byKey.AddOrUpdate(
            role.Key,
            _ => role,
            (_, existingRole) =>
            {
                existingRole.Name = role.Name;
                existingRole.Description = role.Description;
                existingRole.Permissions = [.. role.Permissions];
                existingRole.UpdatedAt = role.UpdatedAt;
                return existingRole;
            });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_byKey.TryGetValue(key, out var existing) && existing.IsSystem)
        {
            throw new InvalidOperationException(
                $"Role '{key}' is a system role and cannot be deleted.");
        }
        _byKey.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task UpsertSystemRoleAsync(Role role, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!role.IsSystem)
        {
            throw new InvalidOperationException(
                "UpsertSystemRoleAsync only accepts roles with IsSystem=true. Use UpsertAsync for custom roles.");
        }

        role.UpdatedAt = DateTimeOffset.UtcNow;

        _byKey.AddOrUpdate(
            role.Key,
            _ => role,
            (_, existing) =>
            {
                // Keep the seeded id stable; overwrite the rest so permission
                // additions in a later release land on existing installs.
                existing.Name = role.Name;
                existing.Description = role.Description;
                existing.Permissions = [.. role.Permissions];
                existing.UpdatedAt = role.UpdatedAt;
                return existing;
            });
        return Task.CompletedTask;
    }
}
