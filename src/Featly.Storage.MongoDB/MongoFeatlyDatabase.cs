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
internal sealed class MongoFeatlyDatabase(IMongoDatabase database)
{
    private static readonly Lock RegistrationLock = new();
    private static bool s_classMapsRegistered;

    public IMongoDatabase Database { get; } = database ?? throw new ArgumentNullException(nameof(database));

    public IMongoCollection<Project> Projects => Database.GetCollection<Project>(MongoCollectionNames.Projects);

    public IMongoCollection<Environment> Environments => Database.GetCollection<Environment>(MongoCollectionNames.Environments);

    public IMongoCollection<Flag> Flags => Database.GetCollection<Flag>(MongoCollectionNames.Flags);

    /// <summary>
    /// Registers every entity's <c>BsonClassMap</c> exactly once per process.
    /// The driver's registration APIs are static and throw on a duplicate
    /// registration, so every store construction path must funnel through
    /// this guard rather than calling <see cref="MongoClassMaps.RegisterAll"/>
    /// directly.
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
