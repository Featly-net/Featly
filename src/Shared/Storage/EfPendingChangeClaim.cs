using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.EntityFramework;

/// <summary>
/// EF Core helpers shared by the relational providers (SQLite, Postgres) so a
/// provider-agnostic atomic operation is written once. Compiled into each
/// provider assembly as a linked source file — ADR-0026 keeps the DbContext
/// internal and per-provider, so there is no shared assembly to host this.
/// </summary>
internal static class EfPendingChangeClaim
{
    /// <summary>
    /// Atomically transitions a change's status with a single conditional
    /// <c>UPDATE ... WHERE status=@from</c> (issue #237). On a shared database
    /// only one concurrent writer's update matches, so exactly one caller claims
    /// the change; the method returns whether this call performed the transition.
    /// </summary>
    public static async Task<bool> TryClaimStatusAsync<TContext>(
        this IDbContextFactory<TContext> factory,
        Guid id,
        ChangeStatus from,
        ChangeStatus to,
        CancellationToken ct)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var affected = await db.Set<PendingChange>()
            .Where(c => c.Id == id && c.Status == from)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Status, to)
                    .SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false);
        return affected == 1;
    }
}
