using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoRoleStore(MongoFeatlyDatabase database) : IRoleStore
{
    public async Task<Role?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return await database.Roles
            .Find(r => r.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await database.Roles
            .Find(r => r.Id == id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct) =>
        await database.Roles
            .Find(FilterDefinition<Role>.Empty)
            .SortBy(r => r.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task UpsertAsync(Role role, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (role.IsSystem)
        {
            throw new InvalidOperationException(
                "Cannot create a new role with IsSystem=true through UpsertAsync. Use UpsertSystemRoleAsync for the seed path.");
        }

        var existing = await database.Roles
            .Find(r => r.Key == role.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is not null && existing.IsSystem)
        {
            throw new InvalidOperationException(
                $"Role '{role.Key}' is a system role and cannot be mutated through UpsertAsync. Use UpsertSystemRoleAsync (seed-only path) instead.");
        }

        await UpsertCoreAsync(existing, role, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var existing = await database.Roles
            .Find(r => r.Key == key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        if (existing.IsSystem)
        {
            throw new InvalidOperationException($"Role '{key}' is a system role and cannot be deleted.");
        }

        await database.Roles.DeleteOneAsync(r => r.Id == existing.Id, ct).ConfigureAwait(false);
    }

    public async Task UpsertSystemRoleAsync(Role role, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!role.IsSystem)
        {
            throw new InvalidOperationException(
                "UpsertSystemRoleAsync only accepts roles with IsSystem=true. Use UpsertAsync for custom roles.");
        }

        var existing = await database.Roles
            .Find(r => r.Key == role.Key)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        await UpsertCoreAsync(existing, role, ct).ConfigureAwait(false);
    }

    private async Task UpsertCoreAsync(Role? existing, Role role, CancellationToken ct)
    {
        role.UpdatedAt = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            await database.Roles.InsertOneAsync(role, cancellationToken: ct).ConfigureAwait(false);
            return;
        }

        // Keep the existing id stable — for the seed path in particular, a
        // later release can add permissions to a system role's template and
        // have that land on installs that already seeded an earlier version.
        var replacement = new Role
        {
            Id = existing.Id,
            Key = existing.Key,
            Name = role.Name,
            Description = role.Description,
            IsSystem = existing.IsSystem,
            Permissions = role.Permissions,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = role.UpdatedAt,
        };

        await database.Roles.ReplaceOneAsync(r => r.Id == existing.Id, replacement, cancellationToken: ct).ConfigureAwait(false);
    }
}
