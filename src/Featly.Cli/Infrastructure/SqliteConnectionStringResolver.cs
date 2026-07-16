namespace Featly.Cli.Infrastructure;

/// <summary>
/// Resolves the SQLite connection string for the offline <c>db</c> commands,
/// in precedence order: explicit <c>--connection-string</c> &gt; the
/// <c>FEATLY_SQLITE</c> environment variable &gt; the built-in default
/// (<c>Data Source=featly.db</c>, matching the server's storage default).
/// </summary>
internal static class SqliteConnectionStringResolver
{
    /// <summary>Environment variable consulted when no explicit value is passed.</summary>
    public const string EnvVarName = "FEATLY_SQLITE";

    /// <summary>Default connection string when nothing else is supplied.</summary>
    public const string Default = "Data Source=featly.db";

    /// <summary>
    /// Resolves the effective connection string. A bare value with no <c>=</c>
    /// (for example <c>./featly.db</c>) is treated as a file path and wrapped as
    /// <c>Data Source=&lt;path&gt;</c> for convenience.
    /// </summary>
    public static string Resolve(string? optionValue)
    {
        var raw = !string.IsNullOrWhiteSpace(optionValue)
            ? optionValue
            : System.Environment.GetEnvironmentVariable(EnvVarName);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Default;
        }

        return raw.Contains('=', StringComparison.Ordinal)
            ? raw
            : $"Data Source={raw}";
    }
}
