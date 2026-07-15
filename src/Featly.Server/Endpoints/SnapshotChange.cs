using ChangeNotification = Featly.Storage.ChangeNotification;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Endpoints;

/// <summary>
/// Announces a change to an environment's SDK snapshot — the flags, segments,
/// configs and experiments <c>GET /api/sdk/config</c> serves.
/// </summary>
/// <remarks>
/// <para>
/// The two halves are deliberately one call (issue #228). Bumping
/// <see cref="Environment.ConfigVersion"/> is what makes the SDK's ETag change,
/// so a write that notifies without bumping would push connected clients to
/// re-fetch and then hand them a 304 — they would never see the change. Pairing
/// them means a new write path cannot get one and forget the other.
/// </para>
/// <para>
/// Call this from <b>every</b> path that alters the snapshot. The ETag used to be
/// derived from <c>max(UpdatedAt)</c> across the four tables, which caught any
/// write for free; a counter is far cheaper to read but only tracks writes that
/// announce themselves.
/// </para>
/// </remarks>
internal static class SnapshotChange
{
    /// <summary>
    /// Bumps the environment's snapshot version (so SDK caches invalidate) and
    /// pushes a change notification to connected SSE clients.
    /// </summary>
    public static async Task AnnounceAsync(
        StorageFacade store,
        Guid environmentId,
        string entityType,
        string key,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        await store.Environments.BumpConfigVersionAsync(environmentId, ct).ConfigureAwait(false);
        await store.Changes.NotifyAsync(
            new ChangeNotification(environmentId, entityType, key, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
    }
}
