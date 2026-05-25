namespace Featly.Storage.Sqlite;

/// <summary>
/// Configures the SQLite Featly store. Populated either inline via
/// <c>AddFeatlySqliteStore(opts =&gt; ...)</c> or bound from configuration
/// under <c>Featly:Storage:Sqlite</c>.
/// </summary>
public sealed class SqliteFeatlyStoreOptions
{
    /// <summary>Configuration section name when binding from <c>IConfiguration</c>.</summary>
    public const string SectionName = "Featly:Storage:Sqlite";

    /// <summary>
    /// ADO.NET connection string for SQLite. Defaults to a local file
    /// <c>featly.db</c> in the host's content root — Hangfire-style quickstart.
    /// Override with anything Microsoft.Data.Sqlite accepts, for example
    /// <c>"Data Source=:memory:;Cache=Shared"</c> for in-memory tests.
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=featly.db";

    /// <summary>
    /// When <c>true</c> (default), Featly applies pending EF Core migrations
    /// at startup. Disable for production deployments where a DBA owns schema
    /// changes — run <c>featly db migrate</c> via the CLI instead (lands in M12).
    /// </summary>
    public bool AutoMigrate { get; set; } = true;
}
