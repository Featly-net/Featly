using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Featly.Storage.Postgres;

/// <summary>
/// <see cref="IChangeNotifier"/> for the PostgreSQL provider (ADR-0026, issue
/// #258): local subscribers fan out through the same <see cref="InProcessChangeNotifier"/>
/// every provider uses, but every notification is also broadcast through
/// Postgres <c>LISTEN</c>/<c>NOTIFY</c> so replicas other than the one that made
/// the change hear about it too.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NotifyAsync"/> only publishes — it does not fan out to local
/// subscribers directly. Delivery to local subscribers happens exclusively
/// through <see cref="DispatchLocallyAsync"/>, which
/// <see cref="PostgresChangeListenerHostedService"/> calls when a notification
/// arrives on the channel. Because Postgres delivers a channel's notifications
/// to every session listening on it — including, on a separate connection, the
/// session that issued the <c>NOTIFY</c> — the replica that raised a change
/// hears about it back through the exact same path as every other replica.
/// One symmetric fan-out path, not two, so there is nothing to deduplicate and
/// no risk of a double delivery to local subscribers.
/// </para>
/// <para>
/// The trade-off is that even local delivery has a round-trip through Postgres.
/// That is a deliberate simplicity-over-latency choice: this is SSE cache
/// invalidation, not the evaluation hot path, and the round trip is on the
/// order of milliseconds.
/// </para>
/// </remarks>
internal sealed class PostgresChangeNotifier(IDbContextFactory<FeatlyDbContext> contextFactory) : IChangeNotifier
{
    /// <summary>The Postgres channel every replica's listener subscribes to.</summary>
    public const string Channel = "featly_changes";

    private readonly InProcessChangeNotifier _local = new();

    public async ValueTask NotifyAsync(ChangeNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = JsonSerializer.Serialize(notification);
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        // pg_notify() (the function form, not the NOTIFY SQL command) accepts
        // both the channel and payload as ordinary parameters, so nothing here
        // is ever concatenated into SQL text.
        await db.Database
            .ExecuteSqlInterpolatedAsync($"SELECT pg_notify({Channel}, {payload})", ct)
            .ConfigureAwait(false);
    }

    public IDisposable Subscribe(Func<ChangeNotification, CancellationToken, ValueTask> handler) => _local.Subscribe(handler);

    /// <summary>
    /// Delivers a notification received on the channel — from any replica,
    /// including this one — to this process's local subscribers. Called only by
    /// <see cref="PostgresChangeListenerHostedService"/>.
    /// </summary>
    internal ValueTask DispatchLocallyAsync(ChangeNotification notification, CancellationToken ct) =>
        _local.NotifyAsync(notification, ct);
}
