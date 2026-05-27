using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteRoleStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IRoleStore
{
    public async Task<Role?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Roles.AsNoTracking()
            .OrderBy(r => r.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(Role role, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (role.IsSystem)
        {
            throw new InvalidOperationException(
                "Cannot create a new role with IsSystem=true through UpsertAsync. Use UpsertSystemRoleAsync for the seed path.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Roles
            .FirstOrDefaultAsync(r => r.Key == role.Key, ct)
            .ConfigureAwait(false);

        if (existing is not null && existing.IsSystem)
        {
            throw new InvalidOperationException(
                $"Role '{role.Key}' is a system role and cannot be mutated through UpsertAsync. Use UpsertSystemRoleAsync (seed-only path) instead.");
        }

        role.UpdatedAt = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            db.Roles.Add(role);
        }
        else
        {
            existing.Name = role.Name;
            existing.Description = role.Description;
            existing.Permissions = [.. role.Permissions];
            existing.UpdatedAt = role.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Roles
            .FirstOrDefaultAsync(r => r.Key == key, ct)
            .ConfigureAwait(false);
        if (existing is null)
        { return; }
        if (existing.IsSystem)
        {
            throw new InvalidOperationException(
                $"Role '{key}' is a system role and cannot be deleted.");
        }

        db.Roles.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertSystemRoleAsync(Role role, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!role.IsSystem)
        {
            throw new InvalidOperationException(
                "UpsertSystemRoleAsync only accepts roles with IsSystem=true. Use UpsertAsync for custom roles.");
        }

        role.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Roles
            .FirstOrDefaultAsync(r => r.Key == role.Key, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Roles.Add(role);
        }
        else
        {
            // Keep the seeded id stable; overwrite the rest so permission
            // additions in a later release land on existing installs.
            existing.Name = role.Name;
            existing.Description = role.Description;
            existing.Permissions = [.. role.Permissions];
            existing.UpdatedAt = role.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
