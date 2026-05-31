namespace Featly.Server.Settings;

/// <summary>
/// Exposes the merged, effective settings values per the three-layer precedence
/// (ARCHITECTURE.md §15): hardcoded default, then <c>appsettings.json</c>, then
/// the database — the DB singleton wins when present. Values are cached in
/// memory; <see cref="ReloadAsync"/> re-reads the database (called at startup and
/// whenever a settings change notification arrives).
/// </summary>
public interface IFeatlySettingsProvider
{
    /// <summary>The effective webhook retry tuning.</summary>
    FeatlyWebhookSettings Webhook { get; }

    /// <summary>Which precedence layer supplied <see cref="Webhook"/>.</summary>
    FeatlySettingsSource WebhookSource { get; }

    /// <summary>Re-reads the database layer and refreshes the cached effective values.</summary>
    Task ReloadAsync(CancellationToken ct = default);
}
