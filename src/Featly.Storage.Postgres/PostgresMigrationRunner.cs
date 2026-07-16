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
/// Every method opens its own short-lived context against the supplied
/// connection string and disposes it before returning. These operations are
/// destructive-by-nature in the case of <see cref="RollbackAsync"/> and
/// <see cref="DropAsync"/>; the caller is responsible for confirming intent.
/// Applying the schema out of band via this runner is also how a multi-replica
/// deployment is expected to migrate: run it once as a deploy step with
/// <c>AutoMigrate: false</c> on every replica, rather than letting each replica
/// race to migrate the shared database on boot.
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

    /// <summary>
    /// Applies every pending migration so the database schema matches this build.
    /// No-op when the schema is already up to date.
    /// </summary>
    /// <param name="connectionString">An Npgsql connection string.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task MigrateAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        await EfMigrationRunner.MigrateAsync(db, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the applied and pending migration sets for the target database.
    /// </summary>
    /// <param name="connectionString">An Npgsql connection string.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The current <see cref="PostgresMigrationStatus"/>.</returns>
    public static async Task<PostgresMigrationStatus> GetStatusAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        var status = await EfMigrationRunner.GetStatusAsync(db, cancellationToken).ConfigureAwait(false);
        return new PostgresMigrationStatus(status.Applied, status.Pending);
    }

    /// <summary>
    /// Reverts the schema down to <paramref name="targetMigration"/>. Pass
    /// <see cref="InitialDatabaseTarget"/> (<c>"0"</c>) to revert every migration.
    /// </summary>
    /// <param name="connectionString">An Npgsql connection string.</param>
    /// <param name="targetMigration">
    /// The migration identifier (or unambiguous name) to roll back to, or
    /// <see cref="InitialDatabaseTarget"/> to undo all migrations.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task RollbackAsync(
        string connectionString,
        string targetMigration,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        await EfMigrationRunner.RollbackAsync(db, targetMigration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the target database entirely (drops all tables and the migration
    /// history). Irreversible.
    /// </summary>
    /// <param name="connectionString">An Npgsql connection string.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> if a database existed and was deleted; otherwise <c>false</c>.</returns>
    public static async Task<bool> DropAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        return await EfMigrationRunner.DropAsync(db, cancellationToken).ConfigureAwait(false);
    }

    private static FeatlyDbContext CreateContext(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new DbContextOptionsBuilder<FeatlyDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new FeatlyDbContext(options);
    }
}
