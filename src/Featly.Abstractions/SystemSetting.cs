using System.Text.Json;

namespace Featly;

/// <summary>
/// A persisted, DB-overridable settings singleton (ARCHITECTURE.md §15). Each
/// row is one <b>typed singleton</b> — a whole settings aggregate (for example
/// <c>"webhook"</c> or <c>"authorization"</c>) serialized to a JSON
/// <see cref="Payload"/> — keyed by a stable <see cref="Key"/>. This is
/// deliberately not a per-field key/value (EAV) table: storing the aggregate as
/// one row keeps validation and the "changed X from a to b" audit semantics that
/// EAV destroys.
/// </summary>
/// <remarks>
/// The store is intentionally untyped (it round-trips the raw <see cref="Payload"/>);
/// the server layer (<c>IFeatlySettingsProvider</c>) owns the typed
/// (de)serialization, the three-layer precedence merge, and the diff it audits.
/// </remarks>
public sealed class SystemSetting
{
    /// <summary>Stable discriminator for the settings aggregate (e.g. <c>"webhook"</c>).</summary>
    public required string Key { get; init; }

    /// <summary>The settings aggregate serialized as JSON.</summary>
    public required JsonElement Payload { get; set; }

    /// <summary>When the value was last written (server clock).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Identifier of who last wrote the value (user identifier or api-key principal).</summary>
    public string? UpdatedBy { get; set; }
}
