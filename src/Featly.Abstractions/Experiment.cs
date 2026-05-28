namespace Featly;

/// <summary>
/// An A/B experiment layered on an existing <see cref="Flag"/> (ARCHITECTURE.md
/// §16). The flag's splitting rule drives bucketing; the experiment adds a time
/// window, the set of metric event keys to track, and opt-in sticky
/// assignments. While the experiment is active the SDK emits an exposure event
/// whenever it evaluates the underlying flag.
/// </summary>
public sealed class Experiment
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>Unique key (per environment) used by APIs and the dashboard.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; set; }

    /// <summary>Optional hypothesis statement shown in the dashboard.</summary>
    public string? Hypothesis { get; set; }

    /// <summary>Key of the flag this experiment is layered on (resolved within the same environment).</summary>
    public required string FlagKey { get; init; }

    /// <summary>Custom event keys counted as conversions for this experiment.</summary>
    public List<string> MetricKeys { get; set; } = [];

    /// <summary>
    /// When <c>true</c>, the first exposure of a subject persists an
    /// <see cref="Assignment"/> so later weight changes don't migrate already-exposed subjects.
    /// </summary>
    public bool StickyAssignments { get; set; }

    /// <summary>Set when the experiment is started; <c>null</c> while in draft.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Set when the experiment is stopped. An experiment is active when started and not stopped.</summary>
    public DateTimeOffset? StoppedAt { get; set; }

    /// <summary>Environment this experiment runs in.</summary>
    public required Guid EnvironmentId { get; init; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Audit: most recent modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Convenience: an experiment is active when it has started and has not been stopped.</summary>
    public bool IsActive => StartedAt is not null && StoppedAt is null;
}
