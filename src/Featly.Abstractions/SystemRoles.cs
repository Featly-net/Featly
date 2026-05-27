namespace Featly;

/// <summary>
/// Templates for the four immutable system roles seeded on first boot per
/// ARCHITECTURE.md §11. Each call to a factory returns a fresh <see cref="Role"/>
/// with <see cref="Role.IsSystem"/> set to <c>true</c> and the canonical
/// permission set — so the seed code in M6 PR 6C, the dashboard's role list,
/// and tests all read from one source of truth.
/// </summary>
/// <remarks>
/// <para>
/// The role keys (<c>"viewer"</c>, <c>"editor"</c>, <c>"approver"</c>,
/// <c>"admin"</c>) are stable identifiers used by role assignments. They are
/// case-sensitive and lower-case by convention.
/// </para>
/// <para>
/// Permission summaries (mirrors ARCHITECTURE.md §11):
/// <list type="bullet">
///   <item><b>Viewer</b> — every <c>*Read</c> permission plus <c>AuditRead</c>.</item>
///   <item><b>Editor</b> — Viewer + create/update on Flag, Config, Segment, Experiment + <c>ChangeCreate</c>.</item>
///   <item><b>Approver</b> — Editor + <c>ChangeApprove</c>, <c>ChangeReject</c>, <c>ChangeApply</c>.</item>
///   <item><b>Admin</b> — every permission, including governance (users, roles, settings, webhooks, environment lock).</item>
/// </list>
/// </para>
/// </remarks>
public static class SystemRoles
{
    /// <summary>Canonical key for the Viewer role.</summary>
    public const string ViewerKey = "viewer";

    /// <summary>Canonical key for the Editor role.</summary>
    public const string EditorKey = "editor";

    /// <summary>Canonical key for the Approver role.</summary>
    public const string ApproverKey = "approver";

    /// <summary>Canonical key for the Admin role.</summary>
    public const string AdminKey = "admin";

    /// <summary>Returns a fresh template instance for the given system role key, or <c>null</c> if the key is not a system role.</summary>
    public static Role? Template(string key)
    {
        return key switch
        {
            ViewerKey => Viewer(),
            EditorKey => Editor(),
            ApproverKey => Approver(),
            AdminKey => Admin(),
            _ => null,
        };
    }

    /// <summary>Returns fresh templates for all four system roles.</summary>
    public static IReadOnlyList<Role> Templates()
        => [Viewer(), Editor(), Approver(), Admin()];

    private static Role Viewer() => new()
    {
        Id = Guid.NewGuid(),
        Key = ViewerKey,
        Name = "Viewer",
        Description = "Read-only access to flags, configs, segments, experiments, environments, projects, users, roles, audit, settings.",
        IsSystem = true,
        Permissions = [.. ReadPermissions],
    };

    private static Role Editor() => new()
    {
        Id = Guid.NewGuid(),
        Key = EditorKey,
        Name = "Editor",
        Description = "Viewer + create / update on Flag, Config, Segment, Experiment, plus ChangeCreate.",
        IsSystem = true,
        Permissions =
        [
            .. ReadPermissions,
            Permission.FlagCreate, Permission.FlagUpdate,
            Permission.ConfigCreate, Permission.ConfigUpdate,
            Permission.SegmentCreate, Permission.SegmentUpdate,
            Permission.ExperimentCreate, Permission.ExperimentUpdate,
            Permission.ChangeCreate,
        ],
    };

    private static Role Approver() => new()
    {
        Id = Guid.NewGuid(),
        Key = ApproverKey,
        Name = "Approver",
        Description = "Editor + ChangeApprove, ChangeReject, ChangeApply.",
        IsSystem = true,
        Permissions =
        [
            .. ReadPermissions,
            Permission.FlagCreate, Permission.FlagUpdate,
            Permission.ConfigCreate, Permission.ConfigUpdate,
            Permission.SegmentCreate, Permission.SegmentUpdate,
            Permission.ExperimentCreate, Permission.ExperimentUpdate,
            Permission.ChangeCreate, Permission.ChangeApprove, Permission.ChangeReject, Permission.ChangeApply,
        ],
    };

    private static Role Admin() => new()
    {
        Id = Guid.NewGuid(),
        Key = AdminKey,
        Name = "Admin",
        Description = "Full access — every Featly permission. Includes governance (users, roles, settings, webhooks, environment lock).",
        IsSystem = true,
        Permissions = [.. AllPermissions],
    };

    private static readonly Permission[] ReadPermissions =
    [
        Permission.FlagRead,
        Permission.ConfigRead,
        Permission.SegmentRead,
        Permission.ExperimentRead,
        Permission.EnvironmentRead,
        Permission.ProjectRead,
        Permission.ApiKeyRead,
        Permission.UserRead,
        Permission.RoleRead,
        Permission.GroupRead,
        Permission.ApprovalPolicyRead,
        Permission.WebhookRead,
        Permission.ChangeRead,
        Permission.SettingsRead,
        Permission.AuditRead,
    ];

    private static readonly Permission[] AllPermissions = Enum.GetValues<Permission>();
}
