namespace Featly;

/// <summary>
/// A persisted experiment assignment: the variant a subject was bucketed into
/// the first time it was exposed (ARCHITECTURE.md §16). Only written when the
/// <see cref="Experiment.StickyAssignments"/> flag is set; subsequent
/// evaluations read the persisted variant instead of re-bucketing, so a
/// mid-flight weight change doesn't migrate already-exposed subjects.
/// </summary>
public sealed class Assignment
{
    /// <summary>Stable row id.</summary>
    public Guid Id { get; init; }

    /// <summary>The experiment this assignment belongs to.</summary>
    public required Guid ExperimentId { get; init; }

    /// <summary>The subject (typically the targeting key) that was bucketed.</summary>
    public required string SubjectKey { get; init; }

    /// <summary>The variant the subject was assigned to.</summary>
    public required string VariantKey { get; init; }

    /// <summary>When the assignment was first recorded.</summary>
    public DateTimeOffset AssignedAt { get; init; }
}
