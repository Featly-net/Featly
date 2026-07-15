namespace Featly.Storage.Postgres;

/// <summary>
/// Configures the PostgreSQL Featly store. Populated either inline via
/// <c>AddFeatlyPostgresStore(opts =&gt; ...)</c> or bound from configuration
/// under <c>Featly:Storage:Postgres</c>.
/// </summary>
public sealed class PostgresFeatlyStoreOptions
{
    /// <summary>Configuration section name when binding from <c>IConfiguration</c>.</summary>
    public const string SectionName = "Featly:Storage:Postgres";

    /// <summary>
    /// Npgsql connection string. Unlike SQLite there is no sensible local
    /// default — a Postgres deployment always points at a server the operator
    /// chose — so this is required and startup fails fast when it is missing.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// When <c>true</c> (default), Featly applies pending EF Core migrations at
    /// startup. Turn it off where a DBA owns schema changes and run
    /// <c>featly db migrate</c> out of band instead.
    /// </summary>
    /// <remarks>
    /// Worth turning off for the centralized pattern specifically: several
    /// replicas booting at once would each race to migrate the same database.
    /// </remarks>
    public bool AutoMigrate { get; set; } = true;
}
