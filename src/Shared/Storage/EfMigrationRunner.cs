using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Featly.Storage.EntityFramework;

/// <summary>
/// Applied/pending migration identifiers for a schema, oldest first.
/// </summary>
internal sealed record EfMigrationStatus(IReadOnlyList<string> Applied, IReadOnlyList<string> Pending);

/// <summary>
/// EF Core migration mechanics shared by the public, per-provider migration
/// runners (<c>SqliteMigrationRunner</c>, <c>PostgresMigrationRunner</c>) — the
/// offline surface the <c>featly db</c> CLI commands sit on top of.
/// </summary>
/// <remarks>
/// Each provider's runner stays a thin, provider-named public facade: it owns
/// building its own <c>DbContext</c> (<c>UseSqlite</c> vs <c>UseNpgsql</c>,
/// different internal <c>FeatlyDbContext</c> types per ADR-0026) and its own
/// public status record, and hands a <c>createContext</c> factory to these
/// methods — which open the context, run the operation, and dispose it, so the
/// wrapper itself is a one-liner instead of repeating the open/dispose dance per
/// provider. Compiled into each provider assembly as a linked source file, same
/// pattern as the <c>Ef*Store</c> bases.
/// </remarks>
internal static class EfMigrationRunner
{
    /// <summary>
    /// The reserved target that reverts every applied migration, leaving an
    /// empty schema. Mirrors EF Core's <c>Migration.InitialDatabase</c> sentinel.
    /// </summary>
    public const string InitialDatabaseTarget = "0";

    /// <summary>Applies every pending migration. No-op when already up to date.</summary>
    public static async Task MigrateAsync<TContext>(Func<TContext> createContext, CancellationToken ct)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(createContext);
        await using var db = createContext();
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads the applied and pending migration sets.</summary>
    public static async Task<EfMigrationStatus> GetStatusAsync<TContext>(Func<TContext> createContext, CancellationToken ct)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(createContext);
        await using var db = createContext();
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).ToList();
        return new EfMigrationStatus(applied, pending);
    }

    /// <summary>
    /// Reverts the schema down to <paramref name="targetMigration"/>. Pass
    /// <see cref="InitialDatabaseTarget"/> to undo every migration.
    /// </summary>
    public static async Task RollbackAsync<TContext>(Func<TContext> createContext, string targetMigration, CancellationToken ct)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(createContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMigration);
        await using var db = createContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Deletes the target database entirely. Irreversible.</summary>
    /// <returns><c>true</c> if a database existed and was deleted; otherwise <c>false</c>.</returns>
    public static async Task<bool> DropAsync<TContext>(Func<TContext> createContext, CancellationToken ct)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(createContext);
        await using var db = createContext();
        return await db.Database.EnsureDeletedAsync(ct).ConfigureAwait(false);
    }
}
