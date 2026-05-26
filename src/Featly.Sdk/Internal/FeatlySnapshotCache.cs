using System.Collections.Immutable;
using Featly.Engine;

namespace Featly.Sdk.Internal;

/// <summary>
/// Thread-safe holder for the latest <see cref="ConfigSnapshot"/> and the
/// ETag the server returned with it. <see cref="FlagClient"/> reads from
/// here on every evaluation; <see cref="FeatlyConfigSyncService"/> writes
/// to it whenever the server reports a new snapshot.
/// </summary>
internal sealed class FeatlySnapshotCache
{
    private CacheEntry _current = new(
        Snapshot: null,
        Etag: null,
        FlagsByKey: ImmutableDictionary<string, Flag>.Empty,
        SegmentLookup: DictionarySegmentLookup.Empty);

    public CacheEntry Current => Volatile.Read(ref _current);

    public void Replace(ConfigSnapshot snapshot, string? etag)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var flagsBuilder = ImmutableDictionary.CreateBuilder<string, Flag>(StringComparer.Ordinal);
        foreach (var flag in snapshot.Flags)
        {
            flagsBuilder[flag.Key] = flag;
        }

        var segmentsBuilder = ImmutableDictionary.CreateBuilder<string, Segment>(StringComparer.Ordinal);
        foreach (var segment in snapshot.Segments)
        {
            segmentsBuilder[segment.Key] = segment;
        }
        var segmentLookup = new DictionarySegmentLookup(segmentsBuilder.ToImmutable());

        Volatile.Write(ref _current, new CacheEntry(
            Snapshot: snapshot,
            Etag: etag,
            FlagsByKey: flagsBuilder.ToImmutable(),
            SegmentLookup: segmentLookup));
    }

    public Flag? TryGetFlag(string key)
        => Current.FlagsByKey.TryGetValue(key, out var flag) ? flag : null;

    /// <summary>The lookup the engine consults to resolve <c>InSegment</c> conditions.</summary>
    public ISegmentLookup Segments => Current.SegmentLookup;

    internal sealed record CacheEntry(
        ConfigSnapshot? Snapshot,
        string? Etag,
        ImmutableDictionary<string, Flag> FlagsByKey,
        ISegmentLookup SegmentLookup);
}
