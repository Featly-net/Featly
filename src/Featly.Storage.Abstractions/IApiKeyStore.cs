namespace Featly.Storage;

/// <summary>
/// Persistence operations for <see cref="ApiKey"/> entities.
/// </summary>
/// <remarks>
/// The store keeps the Argon2id hash only — plaintext tokens never persist.
/// Authentication flow looks up candidates by <see cref="ApiKey.Prefix"/>
/// (indexed) and the caller (server-side <c>ApiKeyHasher</c>) finishes the
/// constant-time verification.
/// </remarks>
public interface IApiKeyStore
{
    /// <summary>Returns the key with the given row id, or <c>null</c>.</summary>
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns every non-revoked key whose <see cref="ApiKey.Prefix"/> matches.
    /// Multiple keys can share a prefix (8-12 character window of randomness),
    /// so the caller verifies each candidate's hash. Usually returns 0 or 1
    /// row.
    /// </summary>
    Task<IReadOnlyList<ApiKey>> FindCandidatesByPrefixAsync(string prefix, CancellationToken ct);

    /// <summary>Lists every key in the environment (revoked included). Sorted by creation time descending.</summary>
    Task<IReadOnlyList<ApiKey>> ListAsync(Guid environmentId, CancellationToken ct);

    /// <summary>Creates a new key. Throws if the id collides.</summary>
    Task CreateAsync(ApiKey apiKey, CancellationToken ct);

    /// <summary>Marks the key as revoked. Idempotent: missing id is a no-op.</summary>
    Task RevokeAsync(Guid id, string actor, CancellationToken ct);

    /// <summary>
    /// Touches <see cref="ApiKey.LastUsedAt"/> on a successful authentication.
    /// Implementations may batch / debounce these writes — best-effort is fine.
    /// </summary>
    Task TouchLastUsedAsync(Guid id, DateTimeOffset at, CancellationToken ct);
}
