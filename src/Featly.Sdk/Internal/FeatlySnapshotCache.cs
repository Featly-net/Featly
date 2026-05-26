using System.Collections.Immutable;
using Featly.Engine;

namespace Featly.Sdk.Internal;

/// <summary>
/// Thread-safe holder for the latest <see cref="ConfigSnapshot"/> and the
/// ETag the server returned with it. <see cref="FlagClient"/> and
/// <see cref="ConfigClient"/> read from here on every evaluation;
/// <see cref="FeatlyConfigSyncService"/> writes to it whenever the server
/// reports a new snapshot.
/// </summary>
internal sealed class FeatlySnapshotCache
{
    private CacheEntry _current = new(
        Snapshot: null,
        Etag: null,
        FlagsByKey: ImmutableDictionary<string, Flag>.Empty,
        ConfigsByKey: ImmutableDictionary<string, Config>.Empty,
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

        var configsBuilder = ImmutableDictionary.CreateBuilder<string, Config>(StringComparer.Ordinal);
        foreach (var config in snapshot.Configs)
        {
            configsBuilder[config.Key] = config;
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
            ConfigsByKey: configsBuilder.ToImmutable(),
            SegmentLookup: segmentLookup));
    }

    public Flag? TryGetFlag(string key)
        => Current.FlagsByKey.TryGetValue(key, out var flag) ? flag : null;

    public Config? TryGetConfig(string key)
        => Current.ConfigsByKey.TryGetValue(key, out var config) ? config : null;

    /// <summary>The lookup the engine consults to resolve <c>InSegment</c> conditions.</summary>
    public ISegmentLookup Segments => Current.SegmentLookup;

    internal sealed record CacheEntry(
        ConfigSnapshot? Snapshot,
        string? Etag,
        ImmutableDictionary<string, Flag> FlagsByKey,
        ImmutableDictionary<string, Config> ConfigsByKey,
        ISegmentLookup SegmentLookup);
}
