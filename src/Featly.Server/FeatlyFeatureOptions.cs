namespace Featly.Server;

/// <summary>
/// Opt-out toggles for the server's feature areas (ARCHITECTURE.md §1, ADR-0024).
/// Disabling an area removes its <c>/api/admin/*</c> endpoint group (and, via the
/// dashboard meta endpoint, hides its UI), letting a consumer run a flags-only or
/// configs-only deployment with a smaller surface.
/// </summary>
/// <remarks>
/// <para>
/// Every area defaults to <c>true</c>, so existing consumers and the quickstart
/// are unaffected — this is purely opt-out. Gating is surface-level only: the
/// storage schema is identical regardless of toggles, so enabling an area later
/// needs no migration.
/// </para>
/// <para>
/// A small core is always on and not represented here: health, auth, the
/// first-admin bootstrap, projects, environments, API keys, settings, and the
/// SDK endpoints — the rest depends on them.
/// </para>
/// </remarks>
public sealed class FeatlyFeatureOptions
{
    /// <summary>Feature flags admin area (<c>/api/admin/flags</c>).</summary>
    public bool Flags { get; set; } = true;

    /// <summary>Dynamic configuration admin area (<c>/api/admin/configs</c>).</summary>
    public bool Configs { get; set; } = true;

    /// <summary>Reusable segments admin area (<c>/api/admin/segments</c>).</summary>
    public bool Segments { get; set; } = true;

    /// <summary>Experiments admin area (<c>/api/admin/experiments</c>).</summary>
    public bool Experiments { get; set; } = true;

    /// <summary>
    /// Approval workflow admin area — pending changes and per-environment
    /// approval policies (<c>/api/admin/changes</c>, <c>/api/admin/approval-policies</c>).
    /// </summary>
    public bool Approvals { get; set; } = true;

    /// <summary>Outbound webhooks admin area (<c>/api/admin/webhooks</c>).</summary>
    public bool Webhooks { get; set; } = true;

    /// <summary>Audit-log admin area (<c>/api/admin/audit</c>).</summary>
    public bool Audit { get; set; } = true;

    /// <summary>
    /// Custom RBAC admin area — users, roles, groups, role assignments, and
    /// role-upgrade requests.
    /// </summary>
    public bool Rbac { get; set; } = true;
}
