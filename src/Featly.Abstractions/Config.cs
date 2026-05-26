using System.Text.Json;

namespace Featly;

/// <summary>
/// A dynamic configuration value. Shares the targeting engine with
/// <see cref="Flag"/>, but produces a typed value directly instead of
/// selecting a variant.
/// </summary>
public sealed class Config
{
    /// <summary>Stable identifier for the config row.</summary>
    public Guid Id { get; init; }

    /// <summary>Unique key inside an environment.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Value type for client-side casting.</summary>
    public required ConfigType Type { get; set; }

    /// <summary>Fallback value used when no rule matches.</summary>
    public required JsonElement DefaultValue { get; set; }

    /// <summary>
    /// Ordered list of targeting rules. The engine walks them by
    /// <see cref="ConfigRule.Order"/> and the first rule whose conditions all
    /// match wins; its <see cref="ConfigRule.Value"/> is served. When no rule
    /// matches, the engine returns <see cref="DefaultValue"/>.
    /// </summary>
    public List<ConfigRule> Rules { get; set; } = [];

    /// <summary>Environment this config is scoped to.</summary>
    public required Guid EnvironmentId { get; init; }

    /// <summary>Free-form tags used for filtering.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Whether the config is archived.</summary>
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
/// Value type of a <see cref="Config"/>. The engine returns a
/// strongly-typed result based on this declaration.
/// </summary>
public enum ConfigType
{
    /// <summary>String value.</summary>
    String,

    /// <summary>32-bit signed integer.</summary>
    Int,

    /// <summary>64-bit signed integer.</summary>
    Long,

    /// <summary>Double-precision floating point.</summary>
    Double,

    /// <summary>Decimal value (financial precision).</summary>
    Decimal,

    /// <summary>Boolean value.</summary>
    Bool,

    /// <summary>UTC date/time value.</summary>
    DateTime,

    /// <summary>Time span value.</summary>
    TimeSpan,

    /// <summary>Structured JSON payload.</summary>
    Json,
}
