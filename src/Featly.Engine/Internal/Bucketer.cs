namespace Featly.Engine.Internal;

/// <summary>
/// Picks a <see cref="Split"/> by hashing the subject's targeting key. Same
/// subject + same flag + same salt always lands in the same bucket — so as
/// long as the rule's weights don't change, the subject keeps its variant.
/// </summary>
internal static class Bucketer
{
    /// <summary>
    /// Salt folded into the hash so every flag has an independent bucket
    /// distribution. Hard-coded for the engine version; a per-flag salt can be
    /// introduced later via flag metadata without rotating existing assignments.
    /// </summary>
    private const string DefaultSalt = "featly";

    /// <summary>
    /// Returns the matching split for the given subject + flag, or
    /// <c>null</c> when the bucket lands outside the cumulative weights
    /// (which shouldn't happen if weights sum to 100 but is defended against
    /// here).
    /// </summary>
    public static Split? PickSplit(string targetingKey, string flagKey, IReadOnlyList<Split> splits)
    {
        ArgumentNullException.ThrowIfNull(splits);

        if (splits.Count == 0)
        {
            return null;
        }

        var bucket = BucketOf(targetingKey, flagKey);

        // splits is ordered by caller; we walk it cumulatively. Weights are
        // expressed in percentages (0-100); the hash bucket is 0-9999. Scale
        // the weight by 100 so 50 -> 5000.
        var cumulative = 0;
        foreach (var split in splits)
        {
            cumulative += split.Weight * 100;
            if (bucket < cumulative)
            {
                return split;
            }
        }

        // Bucket fell outside the declared weights — last split wins as a
        // safety net (covers rounding when weights sum to 99 instead of 100).
        return splits[^1];
    }

    /// <summary>
    /// Hash the subject + flag + salt into a 0..9999 bucket. Exposed for tests
    /// that need to verify determinism of the bucketing input directly.
    /// </summary>
    public static int BucketOf(string targetingKey, string flagKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);

        // The format matches ARCHITECTURE.md §5: targetingKey + ":" + flagKey + ":" + salt.
        var input = string.Concat(targetingKey, ":", flagKey, ":", DefaultSalt);
        return MurmurHash3.BucketOf10000(input);
    }
}
