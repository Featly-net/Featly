namespace Featly;

/// <summary>
/// Top-level scope grouping a related set of <see cref="Environment"/> instances.
/// In a single-tenant embedded deployment there is usually one Project per
/// host process; in centralized deployments many Projects coexist.
/// </summary>
public sealed class Project
{
    /// <summary>Stable identifier for the row.</summary>
    public Guid Id { get; init; }

    /// <summary>Unique short key (slug). Used in URLs and CLI arguments.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name shown in the dashboard.</summary>
    public required string Name { get; set; }

    /// <summary>Optional long-form description.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// When <c>true</c>, this Project is the default for the deployment and is
    /// protected from accidental deletion. Auto-created on first boot.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
