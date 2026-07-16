using Featly.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres;

/// <summary>
/// Snapshot of the PostgreSQL schema migration state: which migrations the
/// database already has, and which the current build defines but has not yet
/// applied.
/// </summary>
/// <param name="Applied">
/// Migration identifiers recorded in the database's history table, oldest first.
/// </param>
/// <param name="Pending">
/// Migration identifiers compiled into this build but not yet present in the
/// database, in the order they would be applied.
/// </param>
public sealed record PostgresMigrationStatus(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Pending);

/// <summary>
/// Public, offline entry point for operating the Featly PostgreSQL schema
/// without a running host — the surface the <c>featly db</c> CLI commands sit
/// on top of. Mirrors <c>Featly.Storage.Sqlite.SqliteMigrationRunner</c>.
/// </summary>
/// <remarks>
/// <para>
/// The EF Core <c>DbContext</c> stays <c>internal</c> to this assembly on
/// purpose (see <see cref="FeatlyDbContext"/>). This runner is the only public
/// way to drive migrations from outside, so tooling never has to reference an
/// EF Core type directly.
/// </para>
/// <para>
/// Every method takes an Npgsql <c>connectionString</c> and an optional
/// <c>cancellationToken</c>; it opens its own short-lived context against that
/// connection string and disposes it before returning.
/// <see cref="RollbackAsync"/> and <see cref="DropAsync"/> are destructive; the
/// caller is responsible for confirming intent. Applying the schema out of band
/// via this runner is also how a multi-replica deployment is expected to
/// migrate: run it once as a deploy step with <c>AutoMigrate: false</c> on every
/// replica, rather than letting each replica race to migrate the shared
/// database on boot.
/// </para>
/// </remarks>
public static class PostgresMigrationRunner
{
    /// <summary>
    /// The reserved target passed to <see cref="RollbackAsync"/> to revert every
    /// applied migration, leaving an empty schema. Mirrors EF Core's
    /// <c>Migration.InitialDatabase</c> sentinel.
    /// </summary>
    public const string InitialDatabaseTarget = EfMigrationRunner.InitialDatabaseTarget;

    /// <summary>Applies every pending migration. No-op when already up to date.</summary>
    public static Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default) =>
        EfMigrationRunner.MigrateAsync(() => CreateContext(connectionString), cancellationToken);

    /// <summary>Reads the applied and pending migration sets for the target database.</summary>
    public static async Task<PostgresMigrationStatus> GetStatusAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var status = await EfMigrationRunner.GetStatusAsync(() => CreateContext(connectionString), cancellationToken).ConfigureAwait(false);
        return new PostgresMigrationStatus(status.Applied, status.Pending);
    }

    /// <summary>
    /// Reverts the schema down to <paramref name="targetMigration"/> (or
    /// <see cref="InitialDatabaseTarget"/> to undo every migration).
    /// </summary>
    public static Task RollbackAsync(string connectionString, string targetMigration, CancellationToken cancellationToken = default) =>
        EfMigrationRunner.RollbackAsync(() => CreateContext(connectionString), targetMigration, cancellationToken);

    /// <summary>Deletes the target database entirely. Irreversible.</summary>
    /// <returns><c>true</c> if a database existed and was deleted; otherwise <c>false</c>.</returns>
    public static Task<bool> DropAsync(string connectionString, CancellationToken cancellationToken = default) =>
        EfMigrationRunner.DropAsync(() => CreateContext(connectionString), cancellationToken);

    private static FeatlyDbContext CreateContext(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new DbContextOptionsBuilder<FeatlyDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new FeatlyDbContext(options);
    }
}
