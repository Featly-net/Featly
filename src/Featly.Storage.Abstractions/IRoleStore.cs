namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="Role"/> entities. System roles
/// (<see cref="Role.IsSystem"/> == <c>true</c>) are seeded on first boot and
/// cannot be edited or deleted; the store rejects mutations against them.
/// </summary>
public interface IRoleStore
{
    /// <summary>Returns the role with the given key, or <c>null</c>.</summary>
    Task<Role?> GetByKeyAsync(string key, CancellationToken ct);

    /// <summary>Returns the role with the given row id, or <c>null</c>.</summary>
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lists every role (system + custom). Sorted by key.</summary>
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Inserts a new role or updates the existing one matched by
    /// <see cref="Role.Key"/>. System roles cannot be mutated through this
    /// method except by the seed path — implementations throw
    /// <see cref="InvalidOperationException"/> on any other write attempt.
    /// </summary>
    Task UpsertAsync(Role role, CancellationToken ct);

    /// <summary>
    /// Deletes a custom role by key. System roles cannot be deleted —
    /// implementations throw <see cref="InvalidOperationException"/>.
    /// Idempotent for missing keys.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>
    /// Seed-only path that bypasses the system-role write guard. Used by the
    /// bootstrap hosted service to insert the four <see cref="SystemRoles"/>
    /// templates on first boot. Subsequent boots upsert (no-op if unchanged).
    /// </summary>
    Task UpsertSystemRoleAsync(Role role, CancellationToken ct);
}
