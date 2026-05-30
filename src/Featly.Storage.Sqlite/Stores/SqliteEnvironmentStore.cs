using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Sqlite.Stores;

internal sealed class SqliteEnvironmentStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IEnvironmentStore
{
    public async Task<Environment?> GetByKeyAsync(Guid projectId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Environments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<Environment?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Environments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Environment?> GetDefaultAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Environments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.IsDefault, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Environment>> ListAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Environments.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task CreateAsync(Environment environment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environment);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var keyTaken = await db.Environments
            .AnyAsync(e => e.ProjectId == environment.ProjectId && e.Key == environment.Key, ct)
            .ConfigureAwait(false);
        if (keyTaken)
        {
            throw new InvalidOperationException(
                $"An environment with key '{environment.Key}' already exists in project '{environment.ProjectId}'.");
        }

        db.Environments.Add(environment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Environment?> SetReadOnlyAsync(Guid id, bool readOnly, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        existing.ReadOnly = readOnly;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing;
    }

    public async Task UpdateAsync(Environment environment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environment);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Environments.FirstOrDefaultAsync(e => e.Id == environment.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            throw new InvalidOperationException($"Environment '{environment.Key}' not found.");
        }

        existing.Name = environment.Name;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        db.Environments.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
