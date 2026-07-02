namespace Featly.Server.Flags;

/// <summary>Why a flag was flagged as a stale-flag candidate.</summary>
public enum StaleFlagReason
{
    /// <summary>No targeting rules left — the flag always resolves to <see cref="Flag.DefaultVariantKey"/> — and unchanged for a while.</summary>
    NoTargetingRules,

    /// <summary>An active experiment on this flag has recorded no exposures for a while (or ever, since it started).</summary>
    ExperimentStalledNoExposures,

    /// <summary>The flag is archived but an experiment referencing it is still active.</summary>
    ArchivedButExperimentStillActive,
}

/// <summary>One flag flagged as a candidate for cleanup, with the signal that triggered it.</summary>
public sealed record StaleFlagCandidate(string FlagKey, StaleFlagReason Reason, DateTimeOffset SignalAt, string Detail);

/// <summary>
/// Pure, storage-free analysis over already-loaded flags/experiments/exposure
/// data to surface flags worth revisiting or removing. Mirrors
/// <c>ExperimentAnalyticsAggregator</c>'s "aggregate on read" shape: no new
/// tracking, no schema change — just a read-side pass over data the server
/// already has.
/// </summary>
/// <remarks>
/// Deliberately does not attempt to detect "100% rolled out to everyone" via
/// rule semantics (a single unconditional 100%-weighted rule is equivalent to
/// having no rules at all, but general rule/segment combinations are not
/// safely reducible without re-implementing the engine's matching logic).
/// <see cref="StaleFlagReason.NoTargetingRules"/> only fires on the
/// unambiguous case: zero rules.
/// </remarks>
public static class StaleFlagAnalyzer
{
    /// <summary>
    /// Finds stale-flag candidates among <paramref name="flags"/>.
    /// </summary>
    /// <param name="flags">Every flag in the environment (archived included).</param>
    /// <param name="experiments">Every experiment in the environment (any lifecycle state).</param>
    /// <param name="lastExposureByFlagKey">
    /// The most recent exposure timestamp per flag key, as reported by
    /// <c>GET /api/admin/flags/{key}/activity</c>; a missing or <c>null</c>
    /// entry means no exposure has ever been recorded.
    /// </param>
    /// <param name="staleAfter">How long a signal must hold before it counts as stale.</param>
    /// <param name="now">The reference "now" (injected for testability).</param>
    public static IReadOnlyList<StaleFlagCandidate> FindCandidates(
        IReadOnlyList<Flag> flags,
        IReadOnlyList<Experiment> experiments,
        IReadOnlyDictionary<string, DateTimeOffset?> lastExposureByFlagKey,
        TimeSpan staleAfter,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(lastExposureByFlagKey);

        var experimentsByFlag = experiments
            .GroupBy(e => e.FlagKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, IReadOnlyList<Experiment> (g) => [.. g], StringComparer.Ordinal);

        var candidates = new List<StaleFlagCandidate>();
        foreach (var flag in flags)
        {
            var flagExperiments = experimentsByFlag.GetValueOrDefault(flag.Key, []);
            var active = flagExperiments.FirstOrDefault(e => e.IsActive);

            if (flag.Archived)
            {
                if (active is not null)
                {
                    candidates.Add(new StaleFlagCandidate(
                        flag.Key,
                        StaleFlagReason.ArchivedButExperimentStillActive,
                        active.StartedAt ?? flag.UpdatedAt,
                        $"Flag is archived but experiment '{active.Key}' is still active — stop it or restore the flag."));
                }

                continue; // The other two reasons don't apply to an archived flag.
            }

            if (flag.Rules.Count == 0 && now - flag.UpdatedAt >= staleAfter)
            {
                candidates.Add(new StaleFlagCandidate(
                    flag.Key,
                    StaleFlagReason.NoTargetingRules,
                    flag.UpdatedAt,
                    $"No targeting rules and unchanged since {flag.UpdatedAt:yyyy-MM-dd} — consider hardcoding '{flag.DefaultVariantKey}' and removing the flag."));
            }

            if (active is not null)
            {
                var lastExposure = lastExposureByFlagKey.GetValueOrDefault(flag.Key);
                var signal = lastExposure ?? active.StartedAt;
                if (signal is { } s && now - s >= staleAfter)
                {
                    candidates.Add(new StaleFlagCandidate(
                        flag.Key,
                        StaleFlagReason.ExperimentStalledNoExposures,
                        s,
                        lastExposure is null
                            ? $"Experiment '{active.Key}' started {active.StartedAt:yyyy-MM-dd} with no exposures recorded since."
                            : $"Experiment '{active.Key}' has had no exposures since {lastExposure:yyyy-MM-dd}."));
                }
            }
        }

        return candidates;
    }
}
