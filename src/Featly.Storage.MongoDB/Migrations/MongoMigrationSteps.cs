namespace Featly.Storage.MongoDB.Migrations;

/// <summary>
/// The ordered, ever-growing list of migration steps. Append only — steps
/// already shipped must never be removed, renamed, or reordered, since
/// <c>__migrations</c> tracks them by name (ADR-0034).
/// </summary>
internal static class MongoMigrationSteps
{
    public static IReadOnlyList<IMongoMigrationStep> All { get; } =
    [
        new InitialIndexesStep(),
        new SegmentConfigIndexesStep(),
        new RbacIndexesStep(),
        new ApprovalApiKeysIndexesStep(),
    ];
}
