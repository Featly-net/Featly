namespace Featly.Server.Experiments;

/// <summary>
/// Per-variant analytics for an experiment, aggregated on read from the raw
/// <see cref="Event"/> rows (ARCHITECTURE.md section 16). Exposures attribute a
/// subject to the variant it first saw; a conversion is a custom event whose
/// key is one of the experiment's <see cref="Experiment.MetricKeys"/>, credited
/// to the variant that subject was exposed to.
/// </summary>
public sealed record ExperimentAnalytics(
    string ExperimentKey,
    string FlagKey,
    bool IsActive,
    DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt,
    int TotalExposureEvents,
    int TotalExposedSubjects,
    IReadOnlyList<VariantAnalytics> Variants);

/// <summary>Exposure + conversion rollup for a single variant.</summary>
public sealed record VariantAnalytics(
    string VariantKey,
    int ExposureEvents,
    int ExposedSubjects,
    IReadOnlyList<MetricAnalytics> Metrics);

/// <summary>Conversion rollup for one metric within a variant.</summary>
public sealed record MetricAnalytics(
    string MetricKey,
    int Conversions,
    int ConversionEvents,
    double ConversionRate);

/// <summary>
/// Pure aggregation over raw events. Kept storage-free and side-effect-free so
/// it is trivially unit-testable; the endpoint feeds it the exposures and the
/// candidate custom events it pulled from the store.
/// </summary>
public static class ExperimentAnalyticsAggregator
{
    /// <summary>
    /// Rolls exposures + custom events into per-variant counts and conversion
    /// rates for <paramref name="experiment"/>.
    /// </summary>
    /// <param name="experiment">The experiment whose metric keys define conversions.</param>
    /// <param name="exposures">Exposure events for the experiment's flag, ordered by time ascending.</param>
    /// <param name="customEvents">Candidate custom events in the environment; filtered to the metric keys here.</param>
    public static ExperimentAnalytics Aggregate(
        Experiment experiment,
        IReadOnlyList<Event> exposures,
        IReadOnlyList<Event> customEvents)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(exposures);
        ArgumentNullException.ThrowIfNull(customEvents);

        // Attribute each subject to the first variant it was exposed to.
        var subjectVariant = new Dictionary<string, string>(StringComparer.Ordinal);
        var exposureEventsByVariant = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in exposures.OrderBy(e => e.At))
        {
            if (string.IsNullOrEmpty(e.VariantKey))
            {
                continue;
            }

            exposureEventsByVariant[e.VariantKey] = exposureEventsByVariant.GetValueOrDefault(e.VariantKey) + 1;
            subjectVariant.TryAdd(e.SubjectKey, e.VariantKey);
        }

        var exposedSubjectsByVariant = subjectVariant
            .GroupBy(kv => kv.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var metricKeys = experiment.MetricKeys ?? [];
        var relevant = customEvents
            .Where(c => c.CustomKey is not null && metricKeys.Contains(c.CustomKey, StringComparer.Ordinal))
            .ToList();

        var variantKeys = exposureEventsByVariant.Keys
            .Union(exposedSubjectsByVariant.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var variants = new List<VariantAnalytics>(variantKeys.Count);
        foreach (var variant in variantKeys)
        {
            var exposedSubjects = exposedSubjectsByVariant.GetValueOrDefault(variant);
            var metrics = new List<MetricAnalytics>(metricKeys.Count);
            foreach (var metric in metricKeys)
            {
                // Custom events for this metric whose subject was exposed to this variant.
                var attributed = relevant
                    .Where(c => string.Equals(c.CustomKey, metric, StringComparison.Ordinal)
                        && subjectVariant.TryGetValue(c.SubjectKey, out var v)
                        && string.Equals(v, variant, StringComparison.Ordinal))
                    .ToList();

                var conversionEvents = attributed.Count;
                var conversions = attributed
                    .Select(c => c.SubjectKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var rate = exposedSubjects == 0 ? 0d : (double)conversions / exposedSubjects;

                metrics.Add(new MetricAnalytics(metric, conversions, conversionEvents, rate));
            }

            variants.Add(new VariantAnalytics(
                variant,
                exposureEventsByVariant.GetValueOrDefault(variant),
                exposedSubjects,
                metrics));
        }

        return new ExperimentAnalytics(
            experiment.Key,
            experiment.FlagKey,
            experiment.IsActive,
            experiment.StartedAt,
            experiment.StoppedAt,
            TotalExposureEvents: exposureEventsByVariant.Values.Sum(),
            TotalExposedSubjects: subjectVariant.Count,
            variants);
    }
}
