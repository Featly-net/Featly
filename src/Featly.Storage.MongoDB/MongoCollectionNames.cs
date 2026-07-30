namespace Featly.Storage.MongoDB;

/// <summary>
/// One collection per entity, matching the relational providers' one-table-
/// per-entity shape (ADR-0034). Lowercase, matching Mongo naming convention.
/// </summary>
internal static class MongoCollectionNames
{
    public const string Projects = "projects";
    public const string Environments = "environments";
    public const string Flags = "flags";
    public const string Segments = "segments";
    public const string Configs = "configs";
    public const string Users = "users";
    public const string Roles = "roles";
    public const string RoleAssignments = "roleAssignments";
    public const string UserGroups = "userGroups";
    public const string RoleUpgradeRequests = "roleUpgradeRequests";
    public const string PendingChanges = "pendingChanges";
    public const string ApprovalPolicies = "approvalPolicies";
    public const string ApiKeys = "apiKeys";
    public const string SystemSettings = "systemSettings";

    /// <summary>Tracks which <see cref="MongoMigrationRunner"/> steps have been applied — this provider's stand-in for a schema.</summary>
    public const string Migrations = "__migrations";
}
