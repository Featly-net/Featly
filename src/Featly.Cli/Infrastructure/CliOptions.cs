using System.CommandLine;

namespace Featly.Cli.Infrastructure;

/// <summary>
/// Factory methods for shared command-line options. Each call returns a fresh
/// instance so an option is never attached to more than one command (a single
/// <see cref="Option"/> object must not be shared across sibling commands).
/// </summary>
internal static class CliOptions
{
    /// <summary>The connection string (or, for SQLite, a bare file path) for offline commands.</summary>
    public static Option<string?> ConnectionString() =>
        new("--connection-string", "-c")
        {
            Description = "Connection string. For --provider sqlite, a bare value is a file path " +
                $"(falls back to {SqliteConnectionStringResolver.EnvVarName}, then '{SqliteConnectionStringResolver.Default}'). " +
                $"For --provider postgres, an Npgsql connection string is required " +
                $"(falls back to {PostgresConnectionStringResolver.EnvVarName}; no default).",
        };

    /// <summary>The storage provider the offline <c>db</c> commands operate on.</summary>
    public static Option<string> Provider() =>
        new Option<string>("--provider", "-p")
        {
            Description = "Storage provider to operate on.",
            DefaultValueFactory = _ => MigrationRunnerFactory.Sqlite,
        }.AcceptOnlyFromAmong(MigrationRunnerFactory.Sqlite, MigrationRunnerFactory.Postgres);

    /// <summary>Skips the interactive confirmation prompt on destructive commands.</summary>
    public static Option<bool> Yes() =>
        new("--yes", "-y")
        {
            Description = "Skip the confirmation prompt (assume yes). Use in scripts.",
        };

    /// <summary>Base URL of the running Featly server (for the online commands).</summary>
    public static Option<string?> ServerUrl() =>
        new("--server-url", "-s")
        {
            Description = $"Featly server base URL. " +
                $"Falls back to the {ServerConnection.ServerUrlEnv} environment variable, " +
                $"then '{ServerConnection.DefaultServerUrl}'.",
        };

    /// <summary>Admin API key used to authenticate the online commands.</summary>
    public static Option<string?> ApiKey() =>
        new("--api-key", "-k")
        {
            Description = $"Admin API key (Bearer). Falls back to the {ServerConnection.ApiKeyEnv} environment variable.",
        };
}
