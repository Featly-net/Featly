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
        SegmentLookup: DictionarySegmentLookup.Empty,
        ExperimentsByFlagKey: ImmutableDictionary<string, Experiment>.Empty);

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

        // The server only ships active experiments in the snapshot. Index them
        // by the flag they cover so the flag client can check coverage in O(1)
        // on the hot path. If two experiments target the same flag, the last one
        // wins — an edge case the dashboard guards against.
        var experimentsBuilder = ImmutableDictionary.CreateBuilder<string, Experiment>(StringComparer.Ordinal);
        foreach (var experiment in snapshot.Experiments ?? [])
        {
            experimentsBuilder[experiment.FlagKey] = experiment;
        }

        Volatile.Write(ref _current, new CacheEntry(
            Snapshot: snapshot,
            Etag: etag,
            FlagsByKey: flagsBuilder.ToImmutable(),
            ConfigsByKey: configsBuilder.ToImmutable(),
            SegmentLookup: segmentLookup,
            ExperimentsByFlagKey: experimentsBuilder.ToImmutable()));
    }

    public Flag? TryGetFlag(string key)
        => Current.FlagsByKey.TryGetValue(key, out var flag) ? flag : null;

    public Config? TryGetConfig(string key)
        => Current.ConfigsByKey.TryGetValue(key, out var config) ? config : null;

    /// <summary>The lookup the engine consults to resolve <c>InSegment</c> conditions.</summary>
    public ISegmentLookup Segments => Current.SegmentLookup;

    /// <summary>
    /// Returns the active experiment covering <paramref name="flagKey"/>, or
    /// <c>null</c> when none — the hot-path coverage check for exposure emission.
    /// </summary>
    public Experiment? TryGetActiveExperimentForFlag(string flagKey)
        => Current.ExperimentsByFlagKey.TryGetValue(flagKey, out var experiment) ? experiment : null;

    internal sealed record CacheEntry(
        ConfigSnapshot? Snapshot,
        string? Etag,
        ImmutableDictionary<string, Flag> FlagsByKey,
        ImmutableDictionary<string, Config> ConfigsByKey,
        ISegmentLookup SegmentLookup,
        ImmutableDictionary<string, Experiment> ExperimentsByFlagKey);
}
