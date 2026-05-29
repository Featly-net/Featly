namespace Featly;

/// <summary>
/// An API token Featly accepts as authentication. Keys are scoped to a single
/// environment (per ARCHITECTURE.md §10) and carry an <see cref="ApiKeyScope"/>
/// that decides whether the bearer can read snapshots (<see cref="ApiKeyScope.SdkRead"/>)
/// or mutate definitions (<see cref="ApiKeyScope.AdminWrite"/>).
/// </summary>
/// <remarks>
/// <para>
/// The plaintext token is shown to the operator exactly once at creation
/// time. The store keeps only:
/// <list type="bullet">
///   <item>An Argon2id hash of the full token (constant-time verification).</item>
///   <item>The first few characters of the token as a <see cref="Prefix"/> for
///         indexed lookup — checking every stored hash on every request would
///         be O(n) with a ~100ms Argon2 per call.</item>
///   <item>A <see cref="Name"/> the operator picks at creation time so the
///         dashboard can label the key.</item>
/// </list>
/// </para>
/// <para>
/// Revocation is a soft flag (<see cref="Revoked"/>) so audit trails keep
/// pointing at a real row. <see cref="LastUsedAt"/> is touched on each
/// successful authentication.
/// </para>
/// </remarks>
public sealed class ApiKey
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Human-readable label, picked by the operator at creation.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// First few characters of the plaintext token (typically 12 chars including
    /// the <c>featly_</c> prefix). Indexed for O(log n) candidate lookup before
    /// the constant-time Argon2 verification on the candidate's hash.
    /// </summary>
    public required string Prefix { get; init; }

    /// <summary>Argon2id hash of the full plaintext token.</summary>
    public required string Hash { get; init; }

    /// <summary>What the bearer is allowed to do.</summary>
    public required ApiKeyScope Scope { get; init; }

    /// <summary>Environment this key authenticates against.</summary>
    public required Guid EnvironmentId { get; init; }

    /// <summary>
    /// Row id of the <see cref="User"/> this key acts as, or <c>null</c> for a
    /// standalone service principal. When set, a request authenticated with this
    /// key resolves to that user's identity — so RBAC, audit, and the approval
    /// workflow attribute the action to a real person rather than to an anonymous
    /// "api-key:Scope" pseudo-identity (ARCHITECTURE.md §10).
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>Set to <c>true</c> when an admin revokes the key. Revoked keys remain in the table for audit.</summary>
    public bool Revoked { get; set; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: identifier of the actor that created the key.</summary>
    public string CreatedBy { get; init; } = "";

    /// <summary>Most recent successful use (UTC). Touched by the auth pipeline; safe to use for "stale key" reports.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
