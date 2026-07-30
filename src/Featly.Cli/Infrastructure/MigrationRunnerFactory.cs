using Featly.Storage.MongoDB;
using Featly.Storage.Postgres;
using Featly.Storage.Sqlite;
using Featly.Storage.SqlServer;

namespace Featly.Cli.Infrastructure;

/// <summary>Applied/pending migration identifiers, oldest first — the shape the CLI works with regardless of provider.</summary>
internal sealed record MigrationStatusInfo(IReadOnlyList<string> Applied, IReadOnlyList<string> Pending);

/// <summary>
/// Provider-agnostic view over <see cref="SqliteMigrationRunner"/> and
/// <see cref="PostgresMigrationRunner"/>, so the <c>featly db</c> command bodies
/// are written once and dispatch on <c>--provider</c>, instead of duplicating
/// each verb per provider.
/// </summary>
internal interface IMigrationRunner
{
    Task<MigrationStatusInfo> GetStatusAsync(CancellationToken ct);

    Task MigrateAsync(CancellationToken ct);

    Task RollbackAsync(string targetMigration, CancellationToken ct);

    Task<bool> DropAsync(CancellationToken ct);
}

/// <summary>
/// Resolves a <c>--provider</c> value and connection string into the matching
/// <see cref="IMigrationRunner"/>.
/// </summary>
internal static class MigrationRunnerFactory
{
    /// <summary>The <c>--provider</c> value selecting SQLite (the default).</summary>
    public const string Sqlite = "sqlite";

    /// <summary>The <c>--provider</c> value selecting PostgreSQL.</summary>
    public const string Postgres = "postgres";

    /// <summary>The <c>--provider</c> value selecting SQL Server.</summary>
    public const string SqlServer = "sqlserver";

    /// <summary>The <c>--provider</c> value selecting MongoDB.</summary>
    public const string MongoDb = "mongodb";

    /// <summary>
    /// The reserved <c>rollback</c> target that reverts every migration. Both
    /// providers' runners mirror the same EF Core sentinel, so this is provider-
    /// independent.
    /// </summary>
    public const string InitialDatabaseTarget = "0";

    /// <summary>
    /// Resolves the connection string for <paramref name="provider"/> (each
    /// provider's own resolver — they differ in env var, default, and whether a
    /// bare value is a meaningful shorthand) and returns the matching runner.
    /// </summary>
    public static IMigrationRunner Create(string provider, string? connectionStringOption) => provider switch
    {
        Sqlite => new SqliteRunner(SqliteConnectionStringResolver.Resolve(connectionStringOption)),
        Postgres => new PostgresRunner(PostgresConnectionStringResolver.Resolve(connectionStringOption)),
        SqlServer => new SqlServerRunner(SqlServerConnectionStringResolver.Resolve(connectionStringOption)),
        MongoDb => new MongoRunner(MongoConnectionStringResolver.Resolve(connectionStringOption)),
        _ => throw new InvalidOperationException(
            $"Unknown --provider '{provider}'. Use '{Sqlite}', '{Postgres}', '{SqlServer}', or '{MongoDb}'."),
    };

    private sealed class SqliteRunner(string connectionString) : IMigrationRunner
    {
        public async Task<MigrationStatusInfo> GetStatusAsync(CancellationToken ct)
        {
            var status = await SqliteMigrationRunner.GetStatusAsync(connectionString, ct).ConfigureAwait(false);
            return new MigrationStatusInfo(status.Applied, status.Pending);
        }

        public Task MigrateAsync(CancellationToken ct) => SqliteMigrationRunner.MigrateAsync(connectionString, ct);

        public Task RollbackAsync(string targetMigration, CancellationToken ct) =>
            SqliteMigrationRunner.RollbackAsync(connectionString, targetMigration, ct);

        public Task<bool> DropAsync(CancellationToken ct) => SqliteMigrationRunner.DropAsync(connectionString, ct);
    }

    private sealed class PostgresRunner(string connectionString) : IMigrationRunner
    {
        public async Task<MigrationStatusInfo> GetStatusAsync(CancellationToken ct)
        {
            var status = await PostgresMigrationRunner.GetStatusAsync(connectionString, ct).ConfigureAwait(false);
            return new MigrationStatusInfo(status.Applied, status.Pending);
        }

        public Task MigrateAsync(CancellationToken ct) => PostgresMigrationRunner.MigrateAsync(connectionString, ct);

        public Task RollbackAsync(string targetMigration, CancellationToken ct) =>
            PostgresMigrationRunner.RollbackAsync(connectionString, targetMigration, ct);

        public Task<bool> DropAsync(CancellationToken ct) => PostgresMigrationRunner.DropAsync(connectionString, ct);
    }

    private sealed class SqlServerRunner(string connectionString) : IMigrationRunner
    {
        public async Task<MigrationStatusInfo> GetStatusAsync(CancellationToken ct)
        {
            var status = await SqlServerMigrationRunner.GetStatusAsync(connectionString, ct).ConfigureAwait(false);
            return new MigrationStatusInfo(status.Applied, status.Pending);
        }

        public Task MigrateAsync(CancellationToken ct) => SqlServerMigrationRunner.MigrateAsync(connectionString, ct);

        public Task RollbackAsync(string targetMigration, CancellationToken ct) =>
            SqlServerMigrationRunner.RollbackAsync(connectionString, targetMigration, ct);

        public Task<bool> DropAsync(CancellationToken ct) => SqlServerMigrationRunner.DropAsync(connectionString, ct);
    }

    private sealed class MongoRunner(string connectionString) : IMigrationRunner
    {
        public async Task<MigrationStatusInfo> GetStatusAsync(CancellationToken ct)
        {
            var status = await MongoMigrationRunner.GetStatusAsync(connectionString, ct).ConfigureAwait(false);
            return new MigrationStatusInfo(status.Applied, status.Pending);
        }

        public Task MigrateAsync(CancellationToken ct) => MongoMigrationRunner.MigrateAsync(connectionString, ct);

        public Task RollbackAsync(string targetMigration, CancellationToken ct) =>
            MongoMigrationRunner.RollbackAsync(connectionString, targetMigration, ct);

        public Task<bool> DropAsync(CancellationToken ct) => MongoMigrationRunner.DropAsync(connectionString, ct);
    }
}
