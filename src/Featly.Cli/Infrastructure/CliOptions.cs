using System.CommandLine;

namespace Featly.Cli.Infrastructure;

/// <summary>
/// Factory methods for shared command-line options. Each call returns a fresh
/// instance so an option is never attached to more than one command (a single
/// <see cref="Option"/> object must not be shared across sibling commands).
/// </summary>
internal static class CliOptions
{
    /// <summary>The SQLite connection string / file path for offline commands.</summary>
    public static Option<string?> ConnectionString() =>
        new(
            aliases: ["--connection-string", "-c"],
            description: $"SQLite connection string or file path. " +
                $"Falls back to the {ConnectionStringResolver.EnvVarName} environment variable, " +
                $"then '{ConnectionStringResolver.Default}'.");

    /// <summary>Skips the interactive confirmation prompt on destructive commands.</summary>
    public static Option<bool> Yes() =>
        new(
            aliases: ["--yes", "-y"],
            description: "Skip the confirmation prompt (assume yes). Use in scripts.");

    /// <summary>Base URL of the running Featly server (for the online commands).</summary>
    public static Option<string?> ServerUrl() =>
        new(
            aliases: ["--server-url", "-s"],
            description: $"Featly server base URL. " +
                $"Falls back to the {ServerConnection.ServerUrlEnv} environment variable, " +
                $"then '{ServerConnection.DefaultServerUrl}'.");

    /// <summary>Admin API key used to authenticate the online commands.</summary>
    public static Option<string?> ApiKey() =>
        new(
            aliases: ["--api-key", "-k"],
            description: $"Admin API key (Bearer). Falls back to the {ServerConnection.ApiKeyEnv} environment variable.");
}
