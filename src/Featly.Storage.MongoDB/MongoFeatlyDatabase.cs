using System.Threading;
using Featly.Storage.MongoDB.ClassMaps;
using MongoDB.Driver;

namespace Featly.Storage.MongoDB;

/// <summary>
/// Thin wrapper around <see cref="IMongoDatabase"/> exposing one typed
/// collection per entity — the document-store equivalent of the relational
/// providers' internal <c>FeatlyDbContext</c> (ADR-0034). There is no schema
/// to hold, so this is a stateless accessor, not a unit-of-work: every store
/// method opens the collection it needs and completes independently, mirroring
/// how the relational providers' <c>IDbContextFactory</c> hands out short-lived
/// contexts per operation.
/// </summary>
internal sealed class MongoFeatlyDatabase
{
    private static readonly Lock RegistrationLock = new();
    private static bool s_classMapsRegistered;

    public MongoFeatlyDatabase(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        EnsureClassMapsRegistered();
        Database = database;
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Project> Projects => Database.GetCollection<Project>(MongoCollectionNames.Projects);

    public IMongoCollection<Environment> Environments => Database.GetCollection<Environment>(MongoCollectionNames.Environments);

    public IMongoCollection<Flag> Flags => Database.GetCollection<Flag>(MongoCollectionNames.Flags);

    public IMongoCollection<Segment> Segments => Database.GetCollection<Segment>(MongoCollectionNames.Segments);

    public IMongoCollection<Config> Configs => Database.GetCollection<Config>(MongoCollectionNames.Configs);

    public IMongoCollection<User> Users => Database.GetCollection<User>(MongoCollectionNames.Users);

    public IMongoCollection<Role> Roles => Database.GetCollection<Role>(MongoCollectionNames.Roles);

    public IMongoCollection<RoleAssignment> RoleAssignments => Database.GetCollection<RoleAssignment>(MongoCollectionNames.RoleAssignments);

    public IMongoCollection<UserGroup> UserGroups => Database.GetCollection<UserGroup>(MongoCollectionNames.UserGroups);

    public IMongoCollection<RoleUpgradeRequest> RoleUpgradeRequests => Database.GetCollection<RoleUpgradeRequest>(MongoCollectionNames.RoleUpgradeRequests);

    public IMongoCollection<PendingChange> PendingChanges => Database.GetCollection<PendingChange>(MongoCollectionNames.PendingChanges);

    public IMongoCollection<ApprovalPolicy> ApprovalPolicies => Database.GetCollection<ApprovalPolicy>(MongoCollectionNames.ApprovalPolicies);

    public IMongoCollection<ApiKey> ApiKeys => Database.GetCollection<ApiKey>(MongoCollectionNames.ApiKeys);

    public IMongoCollection<SystemSetting> SystemSettings => Database.GetCollection<SystemSetting>(MongoCollectionNames.SystemSettings);

    /// <summary>
    /// Registers every entity's <c>BsonClassMap</c> exactly once per process.
    /// The driver's registration APIs are static and throw on a duplicate
    /// registration, so this is also called from every other entry point that
    /// builds a database handle without going through this constructor (e.g.
    /// <see cref="MongoMigrationRunner"/>, which needs the maps registered
    /// before it can even open a connection to apply migrations).
    /// </summary>
    public static void EnsureClassMapsRegistered()
    {
        if (s_classMapsRegistered)
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (s_classMapsRegistered)
            {
                return;
            }

            MongoClassMaps.RegisterAll();
            s_classMapsRegistered = true;
        }
    }
}
