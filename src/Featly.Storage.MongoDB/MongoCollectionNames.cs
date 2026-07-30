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
    public const string Experiments = "experiments";
    public const string Events = "events";
    public const string Assignments = "assignments";
    public const string WebhookEndpoints = "webhookEndpoints";
    public const string WebhookDeliveries = "webhookDeliveries";
    public const string AuditEntries = "auditEntries";

    /// <summary>
    /// A capped collection used purely as a Change Streams signal board for
    /// <see cref="MongoChangeNotifier"/> — analogous to the Postgres
    /// provider's <c>pg_notify</c> channel, except Mongo's pub/sub primitive
    /// needs a real (bounded) collection to watch. Documents are never read
    /// back by key; the capped size bound keeps it from growing unbounded.
    /// </summary>
    public const string ChangeNotifications = "changeNotifications";

    /// <summary>Tracks which <see cref="MongoMigrationRunner"/> steps have been applied — this provider's stand-in for a schema.</summary>
    public const string Migrations = "__migrations";
}
