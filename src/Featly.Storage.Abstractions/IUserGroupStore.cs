namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="UserGroup"/> entities. Membership is
/// stored inline on the group (<see cref="UserGroup.MemberUserIds"/>).
/// </summary>
public interface IUserGroupStore
{
    /// <summary>Returns the group with the given key, or <c>null</c>.</summary>
    Task<UserGroup?> GetByKeyAsync(string key, CancellationToken ct);

    /// <summary>Returns the group with the given row id, or <c>null</c>.</summary>
    Task<UserGroup?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lists every group. Sorted by key.</summary>
    Task<IReadOnlyList<UserGroup>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Returns every group that lists <paramref name="userId"/> in its
    /// membership. The permission checker calls this to expand a user into the
    /// set of assignee ids (the user plus its groups).
    /// </summary>
    Task<IReadOnlyList<UserGroup>> ListForMemberAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Inserts a new group or updates the existing one matched by
    /// <see cref="UserGroup.Key"/>. Key is the natural key — id stays stable
    /// across updates.
    /// </summary>
    Task UpsertAsync(UserGroup group, CancellationToken ct);

    /// <summary>Deletes a group by key. Idempotent for a missing key.</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
