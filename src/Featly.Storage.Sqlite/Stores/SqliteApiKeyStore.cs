using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteApiKeyStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IApiKeyStore
{
    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ApiKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiKey>> FindCandidatesByPrefixAsync(string prefix, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ApiKeys.AsNoTracking()
            .Where(k => !k.Revoked && k.Prefix == prefix)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiKey>> ListAsync(Guid environmentId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // SQLite cannot ORDER BY on DateTimeOffset columns directly (stored as
        // long ticks now, but the projection through the value converter is
        // safer than relying on raw column ordering). Pull then sort
        // client-side; admin lists are tiny.
        var rows = await db.ApiKeys.AsNoTracking()
            .Where(k => k.EnvironmentId == environmentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.OrderByDescending(k => k.CreatedAt).ToList();
    }

    public async Task CreateAsync(ApiKey apiKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RevokeAsync(Guid id, string actor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct).ConfigureAwait(false);
        if (existing is null)
        { return; }
        existing.Revoked = true;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task TouchLastUsedAsync(Guid id, DateTimeOffset at, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct).ConfigureAwait(false);
        if (existing is null)
        { return; }
        existing.LastUsedAt = at;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
