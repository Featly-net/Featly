namespace Featly.Storage;

/// <summary>
/// Verifies an audit hash chain (issue #208) given its hashed entries in append
/// order. Shared by every store so the check is identical regardless of provider.
/// </summary>
public static class AuditChainVerifier
{
    /// <summary>
    /// Validates <paramref name="orderedBySequence"/> (ascending
    /// <see cref="AuditEntry.Sequence"/>, hashed entries only): each entry's stored
    /// <see cref="AuditEntry.Hash"/> must match a recomputation of its content, and
    /// each entry's <see cref="AuditEntry.PreviousHash"/> must equal the prior
    /// entry's hash. The first entry's link is exempt (its predecessor may have
    /// been pruned by retention). Returns the first break, if any.
    /// </summary>
    public static AuditChainVerification Verify(IReadOnlyList<AuditEntry> orderedBySequence)
    {
        ArgumentNullException.ThrowIfNull(orderedBySequence);

        string? priorHash = null;
        var checkedCount = 0;
        for (var i = 0; i < orderedBySequence.Count; i++)
        {
            var entry = orderedBySequence[i];

            var recomputed = AuditHash.Compute(entry, entry.PreviousHash);
            if (!string.Equals(recomputed, entry.Hash, StringComparison.Ordinal))
            {
                return new AuditChainVerification(false, checkedCount, entry.Sequence,
                    "Content hash mismatch — the entry was modified after it was written.");
            }

            if (i > 0 && !string.Equals(entry.PreviousHash, priorHash, StringComparison.Ordinal))
            {
                return new AuditChainVerification(false, checkedCount, entry.Sequence,
                    "Broken previous-hash link — an entry was deleted or reordered.");
            }

            priorHash = entry.Hash;
            checkedCount++;
        }

        return AuditChainVerification.Intact(checkedCount);
    }
}
