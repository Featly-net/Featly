namespace Featly.Server.Flags;

/// <summary>Outcome of validating a flag's proposed <see cref="Prerequisite"/> list.</summary>
public sealed record PrerequisiteValidationResult(bool IsValid, string? Error)
{
    /// <summary>A passing validation with no error.</summary>
    public static readonly PrerequisiteValidationResult Ok = new(true, null);

    /// <summary>A failing validation carrying the reason.</summary>
    public static PrerequisiteValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Write-time validation for <see cref="Flag.Prerequisites"/> (ADR-0027): every
/// referenced flag must exist in the same environment, and the resulting
/// prerequisite graph must stay acyclic. Pure and storage-free — the same
/// "aggregate on read"-shaped design as <c>StaleFlagAnalyzer</c> and
/// <c>ExperimentAnalyticsAggregator</c>, taking already-loaded data rather than
/// querying itself.
/// </summary>
/// <remarks>
/// Cycle rejection happens here, at the write boundary, rather than at
/// evaluation time — see ADR-0027's "Alternatives considered" for why: the
/// evaluator can then assume the graph is acyclic and skip runtime
/// cycle-guard bookkeeping on every call.
/// </remarks>
public static class PrerequisiteValidator
{
    /// <summary>
    /// Validates <paramref name="proposedPrerequisites"/> for the flag keyed
    /// <paramref name="flagKey"/> against every other flag already in the
    /// environment (<paramref name="allFlags"/> — active and archived).
    /// </summary>
    public static PrerequisiteValidationResult Validate(
        IReadOnlyList<Flag> allFlags,
        string flagKey,
        IReadOnlyList<Prerequisite> proposedPrerequisites)
    {
        ArgumentNullException.ThrowIfNull(allFlags);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagKey);
        ArgumentNullException.ThrowIfNull(proposedPrerequisites);

        if (proposedPrerequisites.Count == 0)
        {
            return PrerequisiteValidationResult.Ok;
        }

        var knownKeys = new HashSet<string>(allFlags.Select(f => f.Key), StringComparer.Ordinal) { flagKey };
        foreach (var prerequisite in proposedPrerequisites)
        {
            if (!knownKeys.Contains(prerequisite.FlagKey))
            {
                return PrerequisiteValidationResult.Invalid(
                    $"Prerequisite references unknown flag '{prerequisite.FlagKey}'.");
            }
            if (prerequisite.RequiredVariantKeys.Count == 0)
            {
                return PrerequisiteValidationResult.Invalid(
                    $"Prerequisite on '{prerequisite.FlagKey}' must list at least one required variant key.");
            }
        }

        // Edges: every OTHER flag's persisted prerequisites, plus flagKey's
        // proposed ones (its own persisted edges, if any, are superseded by
        // this write — only the proposed set matters going forward).
        var edges = allFlags
            .Where(f => !string.Equals(f.Key, flagKey, StringComparison.Ordinal))
            .ToDictionary(f => f.Key, f => f.Prerequisites.Select(p => p.FlagKey).ToList(), StringComparer.Ordinal);
        edges[flagKey] = [.. proposedPrerequisites.Select(p => p.FlagKey)];

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool HasCycle(string node)
        {
            if (visiting.Contains(node))
            {
                return true;
            }
            if (!visited.Add(node))
            {
                return false;
            }

            visiting.Add(node);
            if (edges.TryGetValue(node, out var dependsOn) && dependsOn.Any(HasCycle))
            {
                return true;
            }
            visiting.Remove(node);
            return false;
        }

        return HasCycle(flagKey)
            ? PrerequisiteValidationResult.Invalid(
                $"Setting this prerequisite would create a cycle through '{flagKey}'.")
            : PrerequisiteValidationResult.Ok;
    }
}
