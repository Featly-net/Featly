using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// One idempotent migration step — Mongo has no schema, so "migrations" here
/// means index creation and one-off document transforms, applied in order and
/// tracked by name in the <c>__migrations</c> collection (ADR-0034). Applying
/// the same step twice must be a safe no-op (e.g. <c>CreateOneAsync</c> on an
/// index that already exists), since <see cref="MongoMigrationRunner"/> only
/// consults its own tracking collection to decide what is pending, not the
/// database's actual state.
/// </summary>
internal interface IMongoMigrationStep
{
    /// <summary>Stable, unique identifier recorded in <c>__migrations</c>. Never rename or reorder once shipped.</summary>
    string Name { get; }

    Task ApplyAsync(IMongoDatabase database, CancellationToken ct);
}
