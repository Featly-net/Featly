namespace Featly.Cli.Infrastructure;

/// <summary>
/// Resolves the MongoDB connection string for the offline <c>db</c> commands
/// (<c>--provider mongodb</c>), in precedence order: explicit
/// <c>--connection-string</c> &gt; the <c>FEATLY_MONGODB</c> environment
/// variable.
/// </summary>
/// <remarks>
/// Unlike <see cref="SqliteConnectionStringResolver"/> there is no default and no
/// bare-path convenience: a MongoDB deployment always points at a replica set the
/// operator chose (mirrors <c>MongoFeatlyStoreOptions</c>, which fails the same
/// way), and "bare value = file path" has no meaning for a network connection
/// string. The connection string must include a database name (e.g.
/// <c>mongodb://host/featly?replicaSet=rs0</c>) — <see cref="MongoMigrationRunner"/>
/// validates that itself.
/// </remarks>
internal static class MongoConnectionStringResolver
{
    /// <summary>Environment variable consulted when no explicit value is passed.</summary>
    public const string EnvVarName = "FEATLY_MONGODB";

    /// <summary>
    /// Resolves the effective connection string, or throws when neither source
    /// supplies one — <see cref="CliRunner"/> surfaces the message as a one-line
    /// error rather than a stack trace.
    /// </summary>
    public static string Resolve(string? optionValue)
    {
        var raw = !string.IsNullOrWhiteSpace(optionValue)
            ? optionValue
            : System.Environment.GetEnvironmentVariable(EnvVarName);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"A MongoDB connection string is required for --provider mongodb. " +
                $"Pass --connection-string, or set the {EnvVarName} environment variable.");
        }

        return raw;
    }
}
