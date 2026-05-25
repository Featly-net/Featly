using System.Collections.Immutable;

namespace Featly.Sdk.Internal;

/// <summary>
/// Thread-safe holder for the latest <see cref="ConfigSnapshot"/> and the
/// ETag the server returned with it. <see cref="FlagClient"/> reads from
/// here on every evaluation; <see cref="FeatlyConfigSyncService"/> writes
/// to it whenever the server reports a new snapshot.
/// </summary>
internal sealed class FeatlySnapshotCache
{
    private CacheEntry _current = new(null, null, ImmutableDictionary<string, Flag>.Empty);

    public CacheEntry Current => Volatile.Read(ref _current);

    public void Replace(ConfigSnapshot snapshot, string? etag)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = ImmutableDictionary.CreateBuilder<string, Flag>(StringComparer.Ordinal);
        foreach (var flag in snapshot.Flags)
        {
            builder[flag.Key] = flag;
        }

        Volatile.Write(ref _current, new CacheEntry(snapshot, etag, builder.ToImmutable()));
    }

    public Flag? TryGetFlag(string key)
        => Current.FlagsByKey.TryGetValue(key, out var flag) ? flag : null;

    internal sealed record CacheEntry(
        ConfigSnapshot? Snapshot,
        string? Etag,
        ImmutableDictionary<string, Flag> FlagsByKey);
}
