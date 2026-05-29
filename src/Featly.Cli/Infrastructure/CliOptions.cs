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
}
