using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres.Stores;

internal sealed class PostgresProjectStore(IDbContextFactory<FeatlyDbContext> contextFactory) : IProjectStore
{
    public async Task<Project?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Project?> GetDefaultAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsDefault, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Projects.AsNoTracking()
            .OrderBy(p => p.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task CreateAsync(Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var keyTaken = await db.Projects.AnyAsync(p => p.Key == project.Key, ct).ConfigureAwait(false);
        if (keyTaken)
        {
            throw new InvalidOperationException($"A project with key '{project.Key}' already exists.");
        }

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            throw new InvalidOperationException($"Project '{project.Key}' not found.");
        }

        existing.Name = project.Name;
        existing.Description = project.Description;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
