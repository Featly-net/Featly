namespace Featly.Storage;

/// <summary>
/// Append-only persistence for <see cref="AuditEntry"/> rows. The admin audit
/// screen queries over these with optional filters.
/// </summary>
public interface IAuditStore
{
    /// <summary>Appends a single audit entry.</summary>
    Task AppendAsync(AuditEntry entry, CancellationToken ct);

    /// <summary>
    /// Returns audit entries newest-first, narrowed by any supplied filter and
    /// capped at <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? entityType = null,
        string? entityKey = null,
        string? actorIdentifier = null,
        Guid? environmentId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 200,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes audit entries strictly older than <paramref name="cutoff"/> and
    /// returns how many were removed. Used by the audit-retention trimmer.
    /// </summary>
    Task<int> PruneOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
}
