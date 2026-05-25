using System.Text.Json;

namespace Featly;

/// <summary>
/// A feature flag. Boolean, string, number, or JSON, scoped to a single environment.
/// </summary>
/// <remarks>
/// Placeholder shape for M1. Rules, variants, and approval state come online
/// across M2 (boolean), M3 (variants and rules), and M8 (approval).
/// </remarks>
public sealed class Flag
{
    /// <summary>Stable identifier for the flag row.</summary>
    public Guid Id { get; init; }

    /// <summary>Unique key inside an environment, used by SDK callers.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name shown in the dashboard.</summary>
    public required string Name { get; set; }

    /// <summary>Optional long-form description.</summary>
    public string? Description { get; set; }

    /// <summary>Value-shape of the flag.</summary>
    public FlagType Type { get; set; }

    /// <summary>Global kill switch. When false the engine returns the default variant with reason Disabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Fallback variant used when no rule matches.</summary>
    public required string DefaultVariantKey { get; set; }

    /// <summary>
    /// The named outcomes this flag can return. The engine selects one of
    /// these by matching rules or by falling back to <see cref="DefaultVariantKey"/>.
    /// </summary>
    public List<Variant> Variants { get; set; } = [];

    /// <summary>Environment this flag is scoped to.</summary>
    public required Guid EnvironmentId { get; init; }

    /// <summary>Free-form tags used for filtering in the dashboard.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Whether the flag is archived (hidden, evaluations fall back to default).</summary>
    public bool Archived { get; set; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification time.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Audit: identifier of the creator.</summary>
    public string CreatedBy { get; init; } = "";

    /// <summary>Audit: identifier of the last editor.</summary>
    public string UpdatedBy { get; set; } = "";
}

/// <summary>
/// The value-shape of a <see cref="Flag"/>.
/// </summary>
public enum FlagType
{
    /// <summary>Boolean on/off flag.</summary>
    Boolean,

    /// <summary>String variant flag.</summary>
    String,

    /// <summary>Numeric variant flag (int, decimal, or double).</summary>
    Number,

    /// <summary>JSON variant flag carrying a structured payload.</summary>
    Json,
}

/// <summary>
/// One named outcome of a <see cref="Flag"/>. Variants carry the actual
/// value the engine returns when a rule selects them.
/// </summary>
public sealed class Variant
{
    /// <summary>Variant key, unique within the flag.</summary>
    public required string Key { get; init; }

    /// <summary>Display name for the dashboard.</summary>
    public required string Name { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>The typed value, encoded as <see cref="JsonElement"/>.</summary>
    public required JsonElement Value { get; set; }
}
