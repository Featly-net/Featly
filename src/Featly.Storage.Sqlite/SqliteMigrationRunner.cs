using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Featly.Storage.Sqlite;

/// <summary>
/// Snapshot of the SQLite schema migration state: which migrations the database
/// already has, and which the current build defines but has not yet applied.
/// </summary>
/// <param name="Applied">
/// Migration identifiers recorded in the database's history table, oldest first.
/// </param>
/// <param name="Pending">
/// Migration identifiers compiled into this build but not yet present in the
/// database, in the order they would be applied.
/// </param>
public sealed record SqliteMigrationStatus(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Pending);

/// <summary>
/// Public, offline entry point for operating the Featly SQLite schema without a
/// running host — the surface the <c>featly db</c> CLI commands sit on top of.
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
/// </para>
/// </remarks>
public static class SqliteMigrationRunner
{
    /// <summary>
    /// The reserved target passed to <see cref="RollbackAsync"/> to revert every
    /// applied migration, leaving an empty schema. Mirrors EF Core's
    /// <c>Migration.InitialDatabase</c> sentinel.
    /// </summary>
    public const string InitialDatabaseTarget = "0";

    /// <summary>
    /// Applies every pending migration so the database schema matches this build.
    /// No-op when the schema is already up to date.
    /// </summary>
    /// <param name="connectionString">A Microsoft.Data.Sqlite connection string.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task MigrateAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the applied and pending migration sets for the target database.
    /// </summary>
    /// <param name="connectionString">A Microsoft.Data.Sqlite connection string.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The current <see cref="SqliteMigrationStatus"/>.</returns>
    public static async Task<SqliteMigrationStatus> GetStatusAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        return new SqliteMigrationStatus(applied, pending);
    }

    /// <summary>
    /// Reverts the schema down to <paramref name="targetMigration"/>. Pass
    /// <see cref="InitialDatabaseTarget"/> (<c>"0"</c>) to revert every migration.
    /// </summary>
    /// <param name="connectionString">A Microsoft.Data.Sqlite connection string.</param>
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
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMigration);
        await using var db = CreateContext(connectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the target database entirely (drops all tables and the schema
    /// history). Irreversible.
    /// </summary>
    /// <param name="connectionString">A Microsoft.Data.Sqlite connection string.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> if a database existed and was deleted; otherwise <c>false</c>.</returns>
    public static async Task<bool> DropAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        return await db.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FeatlyDbContext CreateContext(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new DbContextOptionsBuilder<FeatlyDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new FeatlyDbContext(options);
    }
}
