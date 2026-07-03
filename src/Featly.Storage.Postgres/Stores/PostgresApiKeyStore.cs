using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresApiKeyStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IApiKeyStore
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
        return await db.ApiKeys.AsNoTracking()
            .Where(k => k.EnvironmentId == environmentId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
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
