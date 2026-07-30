namespace Featly.Storage.MongoDB;

/// <summary>
/// Configures the MongoDB Featly store. Populated either inline via
/// <c>AddFeatlyMongoStore(opts =&gt; ...)</c> or bound from configuration
/// under <c>Featly:Storage:MongoDB</c>.
/// </summary>
public sealed class MongoFeatlyStoreOptions
{
    /// <summary>Configuration section name when binding from <c>IConfiguration</c>.</summary>
    public const string SectionName = "Featly:Storage:MongoDB";

    /// <summary>
    /// MongoDB connection string, including the database name (e.g.
    /// <c>mongodb://host/featly?replicaSet=rs0</c>). There is no sensible
    /// local default — a MongoDB deployment always points at a server the
    /// operator chose — so this is required and startup fails fast when it
    /// is missing. Must reference a replica set (ADR-0034): a standalone
    /// <c>mongod</c> is not supported.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// When <c>true</c> (default), Featly applies pending migration steps at
    /// startup (<see cref="MongoMigrationRunner"/>). Turn it off where a DBA
    /// owns schema/index changes and runs them out of band instead.
    /// </summary>
    /// <remarks>
    /// Worth turning off for the centralized pattern specifically: several
    /// replicas booting at once would each race to apply the same steps.
    /// </remarks>
    public bool AutoMigrate { get; set; } = true;
}
