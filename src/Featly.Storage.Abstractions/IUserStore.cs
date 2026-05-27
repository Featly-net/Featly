namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="User"/> entities.
/// </summary>
public interface IUserStore
{
    /// <summary>Returns the user with the given identifier, or <c>null</c>.</summary>
    Task<User?> GetByIdentifierAsync(string identifier, CancellationToken ct);

    /// <summary>Returns the user with the given row id, or <c>null</c>.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Lists every user. Pagination lands when the user base outgrows it.</summary>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Inserts a new user, or updates the existing one matched by
    /// <see cref="User.Identifier"/>. Identifier is the natural key — id stays
    /// stable across updates.
    /// </summary>
    Task UpsertAsync(User user, string actor, CancellationToken ct);

    /// <summary>Marks the user as disabled. Idempotent: missing user is a no-op.</summary>
    Task DisableAsync(string identifier, string actor, CancellationToken ct);
}
