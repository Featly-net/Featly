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
    string? BaselineVariantKey,
    IReadOnlyList<VariantAnalytics> Variants,
    IReadOnlyList<MetricWinner> Winners);

/// <summary>Exposure + conversion rollup for a single variant.</summary>
public sealed record VariantAnalytics(
    string VariantKey,
    int ExposureEvents,
    int ExposedSubjects,
    IReadOnlyList<MetricAnalytics> Metrics);

/// <summary>
/// Conversion rollup for one metric within a variant. <paramref name="PValue"/>
/// is the two-sided two-proportion z-test against the baseline variant
/// (<c>null</c> on the baseline itself, or when the test is undefined —
/// empty arm / zero variance); <paramref name="IsSignificant"/> applies the
/// conventional 0.05 threshold; <paramref name="UpliftVsBaseline"/> is the
/// relative rate change vs the baseline (0.25 = +25%), <c>null</c> when the
/// baseline rate is zero.
/// </summary>
public sealed record MetricAnalytics(
    string MetricKey,
    int Conversions,
    int ConversionEvents,
    double ConversionRate,
    double? PValue = null,
    bool IsSignificant = false,
    double? UpliftVsBaseline = null);

/// <summary>
/// Per-metric verdict: the variant with the highest conversion rate among those
/// significantly better than the baseline, or <c>null</c> while no variant
/// clears the bar.
/// </summary>
public sealed record MetricWinner(string MetricKey, string? VariantKey, double? PValue);

/// <summary>
/// Pure aggregation over raw events. Kept storage-free and side-effect-free so
/// it is trivially unit-testable; the endpoint feeds it the exposures and the
/// candidate custom events it pulled from the store.
/// </summary>
public static class ExperimentAnalyticsAggregator
{
    /// <summary>
    /// Rolls exposures + custom events into per-variant counts, conversion
    /// rates, and per-metric significance against a baseline variant for
    /// <paramref name="experiment"/>.
    /// </summary>
    /// <param name="experiment">The experiment whose metric keys define conversions.</param>
    /// <param name="exposures">Exposure events for the experiment's flag, ordered by time ascending.</param>
    /// <param name="customEvents">Candidate custom events in the environment; filtered to the metric keys here.</param>
    /// <param name="baselineVariantKey">
    /// The control arm the other variants are tested against — the caller passes
    /// the flag's default variant. <c>null</c> (or a key with no exposures)
    /// falls back to the first observed variant in ordinal order.
    /// </param>
    public static ExperimentAnalytics Aggregate(
        Experiment experiment,
        IReadOnlyList<Event> exposures,
        IReadOnlyList<Event> customEvents,
        string? baselineVariantKey = null)
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

        // Resolve the baseline: the caller's choice when it actually has
        // exposures, else the first observed variant (deterministic ordinal
        // order). Null when nothing has been observed at all.
        var baseline = baselineVariantKey is not null && variantKeys.Contains(baselineVariantKey, StringComparer.Ordinal)
            ? baselineVariantKey
            : variantKeys.FirstOrDefault();

        // First pass: raw counts per (variant, metric).
        var counts = new Dictionary<(string Variant, string Metric), (int Conversions, int ConversionEvents)>();
        foreach (var variant in variantKeys)
        {
            foreach (var metric in metricKeys)
            {
                // Custom events for this metric whose subject was exposed to this variant.
                var attributed = relevant
                    .Where(c => string.Equals(c.CustomKey, metric, StringComparison.Ordinal)
                        && subjectVariant.TryGetValue(c.SubjectKey, out var v)
                        && string.Equals(v, variant, StringComparison.Ordinal))
                    .ToList();

                counts[(variant, metric)] = (
                    attributed.Select(c => c.SubjectKey).Distinct(StringComparer.Ordinal).Count(),
                    attributed.Count);
            }
        }

        // Second pass: rates + two-proportion z-test against the baseline arm.
        var variants = new List<VariantAnalytics>(variantKeys.Count);
        foreach (var variant in variantKeys)
        {
            var exposedSubjects = exposedSubjectsByVariant.GetValueOrDefault(variant);
            var isBaseline = string.Equals(variant, baseline, StringComparison.Ordinal);
            var metrics = new List<MetricAnalytics>(metricKeys.Count);
            foreach (var metric in metricKeys)
            {
                var (conversions, conversionEvents) = counts[(variant, metric)];
                var rate = exposedSubjects == 0 ? 0d : (double)conversions / exposedSubjects;

                double? pValue = null;
                double? uplift = null;
                if (!isBaseline && baseline is not null)
                {
                    var baselineSubjects = exposedSubjectsByVariant.GetValueOrDefault(baseline);
                    var (baselineConversions, _) = counts[(baseline, metric)];
                    pValue = SignificanceCalculator.TwoProportionPValue(
                        baselineConversions, baselineSubjects, conversions, exposedSubjects);
                    var baselineRate = baselineSubjects == 0 ? 0d : (double)baselineConversions / baselineSubjects;
                    uplift = baselineRate > 0d ? (rate - baselineRate) / baselineRate : null;
                }

                metrics.Add(new MetricAnalytics(
                    metric, conversions, conversionEvents, rate,
                    pValue, SignificanceCalculator.IsSignificant(pValue), uplift));
            }

            variants.Add(new VariantAnalytics(
                variant,
                exposureEventsByVariant.GetValueOrDefault(variant),
                exposedSubjects,
                metrics));
        }

        // Per-metric verdict: best-rate variant among those significantly
        // *better* than the baseline (positive uplift).
        var winners = new List<MetricWinner>(metricKeys.Count);
        foreach (var metric in metricKeys)
        {
            var best = variants
                .SelectMany(v => v.Metrics
                    .Where(m => string.Equals(m.MetricKey, metric, StringComparison.Ordinal)
                        && m.IsSignificant
                        && m.UpliftVsBaseline > 0d)
                    .Select(m => (v.VariantKey, m.ConversionRate, m.PValue)))
                .OrderByDescending(x => x.ConversionRate)
                .FirstOrDefault();
            winners.Add(new MetricWinner(metric, best.VariantKey, best.VariantKey is null ? null : best.PValue));
        }

        return new ExperimentAnalytics(
            experiment.Key,
            experiment.FlagKey,
            experiment.IsActive,
            experiment.StartedAt,
            experiment.StoppedAt,
            TotalExposureEvents: exposureEventsByVariant.Values.Sum(),
            TotalExposedSubjects: subjectVariant.Count,
            baseline,
            variants,
            winners);
    }
}
