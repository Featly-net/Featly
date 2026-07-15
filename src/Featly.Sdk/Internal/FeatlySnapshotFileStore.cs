using System.Text.Json;

namespace Featly.Sdk.Internal;

/// <summary>
/// Reads and writes the SDK's on-disk snapshot cache and static bootstrap file
/// (issue #238). The cache file wraps the snapshot with its ETag so a restart can
/// resume conditional revalidation; the bootstrap file is a bare
/// <see cref="ConfigSnapshot"/> in the exact shape the server returns from
/// <c>GET /api/sdk/config</c>. All reads fail soft (return <c>null</c>) so a
/// missing or corrupt file never breaks startup.
/// </summary>
internal static class FeatlySnapshotFileStore
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>Persists the snapshot + ETag to <paramref name="path"/> via a temp file + atomic rename.</summary>
    public static async Task SaveCacheAsync(string path, ConfigSnapshot snapshot, string? etag, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, new PersistedSnapshot(etag, snapshot), s_json, ct).ConfigureAwait(false);
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Loads the on-disk cache (snapshot + ETag), or <c>null</c> when missing or unreadable.</summary>
    public static async Task<(ConfigSnapshot Snapshot, string? Etag)?> LoadCacheAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var payload = await JsonSerializer.DeserializeAsync<PersistedSnapshot>(stream, s_json, ct).ConfigureAwait(false);
            return payload?.Snapshot is { } snapshot ? (snapshot, payload.Etag) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Loads the static bootstrap snapshot (bare <see cref="ConfigSnapshot"/>), or <c>null</c> when missing or unreadable.</summary>
    public static async Task<ConfigSnapshot?> LoadBootstrapAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ConfigSnapshot>(stream, s_json, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Seeds <paramref name="cache"/> before the first network fetch when it is
    /// still empty: prefers the on-disk cache, then the static bootstrap file.
    /// Returns the human-readable source it seeded from (for logging), or
    /// <c>null</c> when the cache was already warm or neither file was present.
    /// </summary>
    public static async Task<string?> SeedCacheAsync(FeatlySnapshotCache cache, string? offlineCachePath, string? bootstrapPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (cache.Current.Snapshot is not null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(offlineCachePath))
        {
            var cached = await LoadCacheAsync(offlineCachePath, ct).ConfigureAwait(false);
            if (cached is { } entry)
            {
                cache.Replace(entry.Snapshot, entry.Etag);
                return "on-disk cache";
            }
        }

        if (!string.IsNullOrWhiteSpace(bootstrapPath))
        {
            var bootstrap = await LoadBootstrapAsync(bootstrapPath, ct).ConfigureAwait(false);
            if (bootstrap is not null)
            {
                // No ETag from a static file — the first fetch pulls a full snapshot.
                cache.Replace(bootstrap, etag: null);
                return "bootstrap file";
            }
        }

        return null;
    }

    /// <summary>
    /// Best-effort write of the fresh snapshot to the on-disk cache. Returns
    /// <c>true</c> when written, <c>false</c> when no cache path is configured or
    /// the write failed (a failed cache write never breaks a refresh).
    /// </summary>
    public static async Task<bool> TryPersistCacheAsync(string? offlineCachePath, ConfigSnapshot snapshot, string? etag, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(offlineCachePath))
        {
            return false;
        }

        try
        {
            await SaveCacheAsync(offlineCachePath, snapshot, etag, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record PersistedSnapshot(string? Etag, ConfigSnapshot? Snapshot);
}
