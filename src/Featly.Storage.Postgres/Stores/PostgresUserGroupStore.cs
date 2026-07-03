using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresUserGroupStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IUserGroupStore
{
    public async Task<UserGroup?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.UserGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<UserGroup?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.UserGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserGroup>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.UserGroups.AsNoTracking()
            .OrderBy(g => g.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserGroup>> ListForMemberAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        // EF translates Contains over a primitive collection to a jsonb query.
        return await db.UserGroups.AsNoTracking()
            .Where(g => g.MemberUserIds.Contains(userId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(UserGroup group, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.UserGroups
            .FirstOrDefaultAsync(g => g.Key == group.Key, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.UserGroups.Add(group);
        }
        else
        {
            existing.Name = group.Name;
            existing.Description = group.Description;
            existing.MemberUserIds = [.. group.MemberUserIds];
            existing.UpdatedAt = group.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.UserGroups
            .FirstOrDefaultAsync(g => g.Key == key, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }
        db.UserGroups.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
