namespace Featly;

/// <summary>
/// A human (or service identity) Featly knows about. Created either
/// automatically by the auth pipeline in <c>Open</c> auto-provision mode
/// or explicitly by an admin in <c>Closed</c> mode. Disabled users keep
/// their row but cannot authenticate.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is whatever the server resolves from the authenticated
/// principal — typically an email or OIDC <c>sub</c> from the cookie session,
/// or the name/bound-user of a Bearer API key. It is the join key used by every
/// downstream lookup (role assignments, audit, change comments).
/// </para>
/// </remarks>
public sealed class User
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Auth-stable identifier (email, OIDC sub, basic-auth username, …).
    /// Unique across the system.
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>Human-readable name, defaults to the identifier when unknown.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Optional email for notifications. May equal <see cref="Identifier"/>.</summary>
    public string? Email { get; set; }

    /// <summary>Disabled users keep their row but cannot authenticate.</summary>
    public bool Disabled { get; set; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Identifier of the actor that created the row.</summary>
    public string CreatedBy { get; init; } = "";

    /// <summary>Identifier of the actor that last modified the row.</summary>
    public string UpdatedBy { get; set; } = "";
}
