namespace Featly.Server.Authentication;

/// <summary>
/// Settings that govern Featly's auth pipeline and auto-provisioning. Binds
/// from the <c>Featly:Authorization</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// All values are bootstrap-only for v0.0.x — they apply at startup and
/// before the DB is reachable. Per ADR-016 the runtime-editable settings
/// migrate to a DB-overridable layer in M6+. For now, change them in
/// <c>appsettings.json</c> and restart.
/// </para>
/// </remarks>
public sealed class FeatlyAuthorizationOptions
{
    /// <summary>Configuration section name (<c>Featly:Authorization</c>).</summary>
    public const string SectionName = "Featly:Authorization";

    /// <summary>
    /// Identifier the bootstrap hosted service uses to seed an Admin user on
    /// first boot. When empty no bootstrap user is created — the operator
    /// promotes one manually through the admin API once a real auth scheme
    /// is wired in.
    /// </summary>
    public string BootstrapAdminIdentifier { get; set; } = "";

    /// <summary>
    /// What happens when an authenticated identifier doesn't match any
    /// existing <see cref="User"/>. Defaults to <see cref="AutoProvisionMode.Open"/>
    /// in development (auto-create as Viewer) and <see cref="AutoProvisionMode.Closed"/>
    /// in production (deny). The DI helper picks the contextual default;
    /// override here to be explicit.
    /// </summary>
    public AutoProvisionMode? AutoProvisionMode { get; set; }
}

/// <summary>What Featly does when an authenticated identifier has no matching <c>User</c> row.</summary>
public enum AutoProvisionMode
{
    /// <summary>Auto-create the user as Viewer in the default project. Good for development.</summary>
    Open,

    /// <summary>Reject the request with 403 until an admin pre-creates the user. Recommended for production.</summary>
    Closed,
}
