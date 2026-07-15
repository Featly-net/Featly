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

    /// <summary>
    /// Walks the hash chain in append order and reports whether it is intact
    /// (issue #208): every entry's stored <see cref="AuditEntry.Hash"/> must match
    /// a recomputation of its content, and each entry's
    /// <see cref="AuditEntry.PreviousHash"/> must equal the prior entry's hash.
    /// Legacy rows written before the chain existed (null hash) and the oldest
    /// surviving entry after pruning are tolerated as chain starts.
    /// </summary>
    Task<AuditChainVerification> VerifyChainAsync(CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="IAuditStore.VerifyChainAsync"/>. <see cref="IsIntact"/>
/// is <c>true</c> when no tampering was detected across <see cref="EntriesChecked"/>
/// hashed entries; otherwise <see cref="BrokenAtSequence"/> and
/// <see cref="Detail"/> identify the first broken link.
/// </summary>
public sealed record AuditChainVerification(bool IsIntact, int EntriesChecked, long? BrokenAtSequence, string? Detail)
{
    /// <summary>An intact chain over <paramref name="entriesChecked"/> entries.</summary>
    public static AuditChainVerification Intact(int entriesChecked) => new(true, entriesChecked, null, null);
}
