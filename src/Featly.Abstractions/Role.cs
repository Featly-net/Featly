namespace Featly;

/// <summary>
/// A named bundle of <see cref="Permission"/> values. Featly seeds four
/// immutable system roles on first boot (<see cref="SystemRoles"/>); operators
/// create custom roles by cloning a system template and editing the copy.
/// </summary>
/// <remarks>
/// <para>
/// System roles (<see cref="IsSystem"/> == <c>true</c>) cannot be edited or
/// deleted — that protects the meaning of well-known names like "Viewer".
/// Custom roles are mutable.
/// </para>
/// <para>
/// M6 PR 6A defines the entity shape and storage. The four system roles get
/// seeded into the store on boot in M6 PR 6C.
/// </para>
/// </remarks>
public sealed class Role
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Unique key used by APIs and assignments.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; set; }

    /// <summary>Optional description shown in the dashboard.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// <c>true</c> for the four seeded system roles. The store rejects writes
    /// that try to mutate or delete a system role.
    /// </summary>
    public bool IsSystem { get; init; }

    /// <summary>The permissions this role grants. Union with other matching role assignments determines the effective permission set.</summary>
    public List<Permission> Permissions { get; set; } = [];

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
