namespace Featly.Cli.Infrastructure;

/// <summary>
/// Resolves the SQL Server connection string for the offline <c>db</c> commands
/// (<c>--provider sqlserver</c>), in precedence order: explicit
/// <c>--connection-string</c> &gt; the <c>FEATLY_SQLSERVER</c> environment
/// variable.
/// </summary>
/// <remarks>
/// Unlike <see cref="SqliteConnectionStringResolver"/> there is no default and no
/// bare-path convenience: a SQL Server deployment always points at a server the
/// operator chose (mirrors <c>SqlServerFeatlyStoreOptions</c>, which fails the
/// same way), and "bare value = file path" has no meaning for a network
/// connection string.
/// </remarks>
internal static class SqlServerConnectionStringResolver
{
    /// <summary>Environment variable consulted when no explicit value is passed.</summary>
    public const string EnvVarName = "FEATLY_SQLSERVER";

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
                $"A SQL Server connection string is required for --provider sqlserver. " +
                $"Pass --connection-string, or set the {EnvVarName} environment variable.");
        }

        return raw;
    }
}
