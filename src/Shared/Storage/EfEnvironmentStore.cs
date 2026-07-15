using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.EntityFramework;

/// <summary>
/// Provider-agnostic <see cref="IEnvironmentStore"/> implemented over EF Core.
/// The relational providers (SQLite, Postgres) derive a one-line subclass bound to
/// their own <typeparamref name="TContext"/>. Compiled into each provider assembly
/// as a linked source file (ADR-0026); every query uses
/// <c>Set&lt;Environment&gt;()</c> so it stays context-agnostic.
/// </summary>
internal abstract class EfEnvironmentStore<TContext>(IDbContextFactory<TContext> contextFactory) : IEnvironmentStore
    where TContext : DbContext
{
    public async Task<Environment?> GetByKeyAsync(Guid projectId, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<Environment>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<Environment?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<Environment>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<Environment?> GetDefaultAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<Environment>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.IsDefault, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Environment>> ListAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<Environment>().AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task CreateAsync(Environment environment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environment);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var keyTaken = await db.Set<Environment>()
            .AnyAsync(e => e.ProjectId == environment.ProjectId && e.Key == environment.Key, ct)
            .ConfigureAwait(false);
        if (keyTaken)
        {
            throw new InvalidOperationException(
                $"An environment with key '{environment.Key}' already exists in project '{environment.ProjectId}'.");
        }

        db.Set<Environment>().Add(environment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Environment?> SetReadOnlyAsync(Guid id, bool readOnly, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<Environment>().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
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
        var existing = await db.Set<Environment>().FirstOrDefaultAsync(e => e.Id == environment.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            throw new InvalidOperationException($"Environment '{environment.Key}' not found.");
        }

        existing.Name = environment.Name;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task BumpConfigVersionAsync(Guid id, CancellationToken ct)
    {
        // A single UPDATE ... SET ConfigVersion = ConfigVersion + 1, so two
        // concurrent writers cannot read-modify-write over each other and lose a
        // bump — which would leave SDK clients on a stale snapshot (issue #228).
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.Set<Environment>()
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ConfigVersion, e => e.ConfigVersion + 1), ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<Environment>().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        db.Set<Environment>().Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
