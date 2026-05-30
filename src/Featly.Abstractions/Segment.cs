namespace Featly;

/// <summary>
/// A reusable audience definition. Segments let teams maintain a set of
/// conditions in one place and reference it from many flags via
/// <see cref="ConditionOperator.InSegment"/>.
/// </summary>
/// <remarks>
/// A subject is in the segment when every condition matches (AND across
/// conditions, same semantics as <see cref="Rule.Conditions"/>).
/// </remarks>
public sealed class Segment
{
    /// <summary>Stable identifier for the segment row.</summary>
    public Guid Id { get; init; }

    /// <summary>Unique key inside the environment. Referenced by <c>InSegment</c> conditions.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name shown in the dashboard.</summary>
    public required string Name { get; set; }

    /// <summary>Optional long-form description.</summary>
    public string? Description { get; set; }

    /// <summary>Predicates the subject must satisfy to belong to the segment.</summary>
    public List<Condition> Conditions { get; set; } = [];

    /// <summary>Environment this segment is scoped to.</summary>
    public required Guid EnvironmentId { get; init; }

    /// <summary>Whether the segment is archived (hidden from lists and excluded from the SDK snapshot).</summary>
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
