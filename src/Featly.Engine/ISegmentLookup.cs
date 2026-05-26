namespace Featly.Engine;

/// <summary>
/// Resolves a <see cref="Segment"/> by key for the engine. Implementations
/// typically wrap an in-memory snapshot keyed by segment key — there is no
/// HTTP or IO involved on the hot path.
/// </summary>
public interface ISegmentLookup
{
    /// <summary>
    /// Returns the segment with the given key, or <c>false</c> + <c>null</c>
    /// when missing. The engine treats missing segments as non-matching.
    /// </summary>
    bool TryGet(string key, out Segment? segment);
}

/// <summary>
/// An <see cref="ISegmentLookup"/> backed by a pre-built dictionary. Suitable
/// for snapshot-based evaluation (SDK and server preview).
/// </summary>
public sealed class DictionarySegmentLookup(IReadOnlyDictionary<string, Segment>? segments) : ISegmentLookup
{
    /// <summary>A shared, always-empty lookup. Useful when no segments are configured.</summary>
    public static readonly DictionarySegmentLookup Empty = new(null);

    /// <inheritdoc />
    public bool TryGet(string key, out Segment? segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (segments is null)
        {
            segment = null;
            return false;
        }

        if (segments.TryGetValue(key, out var found) && found is not null)
        {
            segment = found;
            return true;
        }

        segment = null;
        return false;
    }
}
