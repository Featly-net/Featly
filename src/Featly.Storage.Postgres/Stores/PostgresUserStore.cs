using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresUserStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IUserStore
{
    public async Task<User?> GetByIdentifierAsync(string identifier, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Identifier == identifier, ct)
            .ConfigureAwait(false);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.Identifier)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(User user, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = actor;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Identifier == user.Identifier, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.Users.Add(user);
        }
        else
        {
            existing.DisplayName = user.DisplayName;
            existing.Email = user.Email;
            existing.Disabled = user.Disabled;
            existing.UpdatedAt = user.UpdatedAt;
            existing.UpdatedBy = user.UpdatedBy;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DisableAsync(string identifier, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Identifier == identifier, ct)
            .ConfigureAwait(false);
        if (existing is null)
        { return; }

        existing.Disabled = true;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedBy = actor;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
