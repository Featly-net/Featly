namespace Featly;

/// <summary>
/// Every action that the Featly admin surface can require. Roles carry a set
/// of <see cref="Permission"/> values; the permission checker returns
/// whether the resolved user, in the context of a project / environment,
/// holds the asked-for permission.
/// </summary>
/// <remarks>
/// The enum is defined in <c>Featly.Abstractions</c> so endpoint code can
/// reference it without taking a server-side dependency. New permissions
/// land at the end so existing rows on disk keep their numeric values
/// stable. M6 PR 6A defines them; M6 PR 6C wires enforcement on every
/// admin endpoint.
/// </remarks>
public enum Permission
{
    /// <summary>Read flags.</summary>
    FlagRead,
    /// <summary>Create a new flag.</summary>
    FlagCreate,
    /// <summary>Update an existing flag (name, variants, rules, …).</summary>
    FlagUpdate,
    /// <summary>Archive a flag.</summary>
    FlagArchive,

    /// <summary>Read configs.</summary>
    ConfigRead,
    /// <summary>Create a new config.</summary>
    ConfigCreate,
    /// <summary>Update an existing config.</summary>
    ConfigUpdate,
    /// <summary>Archive a config.</summary>
    ConfigArchive,

    /// <summary>Read segments.</summary>
    SegmentRead,
    /// <summary>Create a new segment.</summary>
    SegmentCreate,
    /// <summary>Update an existing segment.</summary>
    SegmentUpdate,
    /// <summary>Archive a segment.</summary>
    SegmentArchive,

    /// <summary>Read experiments.</summary>
    ExperimentRead,
    /// <summary>Create a new experiment.</summary>
    ExperimentCreate,
    /// <summary>Update an existing experiment.</summary>
    ExperimentUpdate,
    /// <summary>Start an experiment.</summary>
    ExperimentStart,
    /// <summary>Stop an experiment.</summary>
    ExperimentStop,

    /// <summary>Read environments.</summary>
    EnvironmentRead,
    /// <summary>Create a new environment in a project.</summary>
    EnvironmentCreate,
    /// <summary>Update environment metadata.</summary>
    EnvironmentUpdate,
    /// <summary>Toggle the ReadOnly flag on an environment.</summary>
    EnvironmentLock,

    /// <summary>Read projects.</summary>
    ProjectRead,
    /// <summary>Create a new project.</summary>
    ProjectCreate,
    /// <summary>Update project metadata.</summary>
    ProjectUpdate,

    /// <summary>Read API keys (metadata only — never the secret).</summary>
    ApiKeyRead,
    /// <summary>Mint a new API key.</summary>
    ApiKeyCreate,
    /// <summary>Revoke an existing API key.</summary>
    ApiKeyRevoke,

    /// <summary>Read users.</summary>
    UserRead,
    /// <summary>Create a new user (when in Closed auto-provision mode).</summary>
    UserCreate,
    /// <summary>Change a user's role assignments.</summary>
    UserUpdateRole,
    /// <summary>Disable a user.</summary>
    UserDisable,

    /// <summary>Read roles.</summary>
    RoleRead,
    /// <summary>Create a new custom role.</summary>
    RoleCreate,
    /// <summary>Update an existing custom role.</summary>
    RoleUpdate,
    /// <summary>Delete a custom role.</summary>
    RoleDelete,
    /// <summary>Read user groups.</summary>
    GroupRead,
    /// <summary>Create a new user group.</summary>
    GroupCreate,
    /// <summary>Update an existing user group.</summary>
    GroupUpdate,
    /// <summary>Delete a user group.</summary>
    GroupDelete,

    /// <summary>Read the approval policy for an environment.</summary>
    ApprovalPolicyRead,
    /// <summary>Update the approval policy for an environment.</summary>
    ApprovalPolicyUpdate,

    /// <summary>Read the audit log.</summary>
    AuditRead,

    /// <summary>Read webhooks.</summary>
    WebhookRead,
    /// <summary>Create a new webhook.</summary>
    WebhookCreate,
    /// <summary>Update an existing webhook.</summary>
    WebhookUpdate,
    /// <summary>Delete a webhook.</summary>
    WebhookDelete,

    /// <summary>Read pending changes.</summary>
    ChangeRead,
    /// <summary>Create a pending change.</summary>
    ChangeCreate,
    /// <summary>Approve a pending change.</summary>
    ChangeApprove,
    /// <summary>Reject a pending change.</summary>
    ChangeReject,
    /// <summary>Apply an approved change.</summary>
    ChangeApply,
    /// <summary>Bypass the approval flow with an emergency override.</summary>
    ChangeBypass,

    /// <summary>Read DB-overridable settings.</summary>
    SettingsRead,
    /// <summary>Update DB-overridable settings.</summary>
    SettingsUpdate,
}
