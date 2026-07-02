using AwesomeAssertions;
using Featly.Server.Experiments;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Unit tests for the pure <see cref="ExperimentAnalyticsAggregator"/>: exposure
/// attribution (first variant a subject saw), per-variant counts, and
/// conversion-rate math credited to the exposed variant.
/// </summary>
public class ExperimentAnalyticsAggregatorTests
{
    private static readonly Guid Env = Guid.NewGuid();

    [Fact]
    public void Counts_exposures_and_conversions_per_variant()
    {
        var experiment = NewExperiment(metricKeys: ["checkout.completed"]);

        // green: 2 subjects exposed (g1, g2), g1 converts. blue: 2 subjects (b1, b2), both convert.
        var exposures = new[]
        {
            Exposure("g1", "green"),
            Exposure("g2", "green"),
            Exposure("b1", "blue"),
            Exposure("b2", "blue"),
        };
        var customs = new[]
        {
            Custom("g1", "checkout.completed"),
            Custom("b1", "checkout.completed"),
            Custom("b2", "checkout.completed"),
        };

        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs);

        result.TotalExposedSubjects.Should().Be(4);
        result.TotalExposureEvents.Should().Be(4);
        result.Variants.Should().HaveCount(2);

        var green = result.Variants.Single(v => v.VariantKey == "green");
        green.ExposedSubjects.Should().Be(2);
        var greenMetric = green.Metrics.Single();
        greenMetric.Conversions.Should().Be(1);
        greenMetric.ConversionRate.Should().Be(0.5);

        var blue = result.Variants.Single(v => v.VariantKey == "blue");
        blue.ExposedSubjects.Should().Be(2);
        var blueMetric = blue.Metrics.Single();
        blueMetric.Conversions.Should().Be(2);
        blueMetric.ConversionRate.Should().Be(1.0);
    }

    [Fact]
    public void Attributes_subject_to_first_variant_seen()
    {
        var experiment = NewExperiment(metricKeys: ["m"]);

        // s1 first sees green, then (erroneously) blue — attribution stays green.
        var exposures = new[]
        {
            Exposure("s1", "green", atOffsetSeconds: 0),
            Exposure("s1", "blue", atOffsetSeconds: 10),
        };
        var customs = new[] { Custom("s1", "m") };

        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs);

        result.TotalExposedSubjects.Should().Be(1);
        var green = result.Variants.Single(v => v.VariantKey == "green");
        green.ExposedSubjects.Should().Be(1);
        green.Metrics.Single().Conversions.Should().Be(1);
        // blue saw an exposure event but no distinct subject attributed to it.
        var blue = result.Variants.Single(v => v.VariantKey == "blue");
        blue.ExposedSubjects.Should().Be(0);
        blue.Metrics.Single().Conversions.Should().Be(0);
    }

    [Fact]
    public void Ignores_custom_events_for_unexposed_subjects_and_unrelated_keys()
    {
        var experiment = NewExperiment(metricKeys: ["checkout.completed"]);

        var exposures = new[] { Exposure("s1", "green") };
        var customs = new[]
        {
            Custom("s1", "unrelated.event"),     // not a metric key
            Custom("never-exposed", "checkout.completed"), // subject never exposed
        };

        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs);

        var green = result.Variants.Single(v => v.VariantKey == "green");
        green.Metrics.Single().Conversions.Should().Be(0);
        green.Metrics.Single().ConversionRate.Should().Be(0);
    }

    [Fact]
    public void Dedupes_repeated_conversions_into_distinct_subjects()
    {
        var experiment = NewExperiment(metricKeys: ["m"]);

        var exposures = new[] { Exposure("s1", "green") };
        var customs = new[] { Custom("s1", "m"), Custom("s1", "m"), Custom("s1", "m") };

        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs);

        var metric = result.Variants.Single().Metrics.Single();
        metric.Conversions.Should().Be(1);       // one distinct subject
        metric.ConversionEvents.Should().Be(3);  // three raw events
        metric.ConversionRate.Should().Be(1.0);
    }

    [Fact]
    public void Empty_events_yield_no_variants()
    {
        var experiment = NewExperiment(metricKeys: ["m"]);
        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, [], []);

        result.TotalExposedSubjects.Should().Be(0);
        result.Variants.Should().BeEmpty();
        result.BaselineVariantKey.Should().BeNull();
        result.Winners.Should().ContainSingle(w => w.MetricKey == "m" && w.VariantKey == null);
    }

    [Fact]
    public void Baseline_carries_no_pvalue_and_the_significantly_better_variant_wins()
    {
        var experiment = NewExperiment(metricKeys: ["checkout.completed"]);

        // control: 10/100 (10%), treatment: 25/100 (25%) — a large, clearly
        // significant lift (mirrors SignificanceCalculatorTests).
        var exposures = new List<Event>();
        var customs = new List<Event>();
        for (var i = 0; i < 100; i++)
        {
            exposures.Add(Exposure("c" + i, "control"));
            if (i < 10) { customs.Add(Custom("c" + i, "checkout.completed")); }
        }
        for (var i = 0; i < 100; i++)
        {
            exposures.Add(Exposure("t" + i, "treatment"));
            if (i < 25) { customs.Add(Custom("t" + i, "checkout.completed")); }
        }

        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs, baselineVariantKey: "control");

        result.BaselineVariantKey.Should().Be("control");

        var control = result.Variants.Single(v => v.VariantKey == "control").Metrics.Single();
        control.PValue.Should().BeNull("the baseline is never tested against itself");
        control.IsSignificant.Should().BeFalse();
        control.UpliftVsBaseline.Should().BeNull();

        var treatment = result.Variants.Single(v => v.VariantKey == "treatment").Metrics.Single();
        treatment.PValue.Should().NotBeNull();
        treatment.PValue!.Value.Should().BeLessThan(SignificanceCalculator.Alpha);
        treatment.IsSignificant.Should().BeTrue();
        treatment.UpliftVsBaseline.Should().BeApproximately(1.5, 0.001); // +150%

        result.Winners.Should().ContainSingle(w => w.MetricKey == "checkout.completed" && w.VariantKey == "treatment");
    }

    [Fact]
    public void No_winner_when_no_variant_beats_the_baseline_significantly()
    {
        var experiment = NewExperiment(metricKeys: ["m"]);

        // control 10/100, treatment 11/100 — noise-level gap, not significant.
        var exposures = new List<Event>();
        var customs = new List<Event>();
        for (var i = 0; i < 100; i++)
        {
            exposures.Add(Exposure("c" + i, "control"));
            if (i < 10) { customs.Add(Custom("c" + i, "m")); }
        }
        for (var i = 0; i < 100; i++)
        {
            exposures.Add(Exposure("t" + i, "treatment"));
            if (i < 11) { customs.Add(Custom("t" + i, "m")); }
        }

        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs, baselineVariantKey: "control");

        var treatment = result.Variants.Single(v => v.VariantKey == "treatment").Metrics.Single();
        treatment.IsSignificant.Should().BeFalse();
        result.Winners.Should().ContainSingle(w => w.VariantKey == null);
    }

    [Fact]
    public void A_variant_significantly_worse_than_baseline_is_never_a_winner()
    {
        var experiment = NewExperiment(metricKeys: ["m"]);

        // control converts far more than treatment — treatment must not win
        // even though the difference is significant (it's a significant loss).
        var exposures = new List<Event>();
        var customs = new List<Event>();
        for (var i = 0; i < 100; i++)
        {
            exposures.Add(Exposure("c" + i, "control"));
            if (i < 40) { customs.Add(Custom("c" + i, "m")); }
        }
        for (var i = 0; i < 100; i++)
        {
            exposures.Add(Exposure("t" + i, "treatment"));
            if (i < 5) { customs.Add(Custom("t" + i, "m")); }
        }

        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs, baselineVariantKey: "control");

        var treatment = result.Variants.Single(v => v.VariantKey == "treatment").Metrics.Single();
        treatment.IsSignificant.Should().BeTrue("the drop is large enough to be significant");
        treatment.UpliftVsBaseline.Should().BeLessThan(0);
        result.Winners.Should().ContainSingle(w => w.VariantKey == null, "a significant loss is not a win");
    }

    [Fact]
    public void Falls_back_to_the_first_observed_variant_when_the_requested_baseline_has_no_exposures()
    {
        var experiment = NewExperiment(metricKeys: ["m"]);
        var exposures = new[] { Exposure("s1", "blue"), Exposure("s2", "green") };
        var customs = Array.Empty<Event>();

        // "control" (e.g. an unreachable flag default) was never actually
        // exposed — the aggregator falls back rather than reporting no baseline.
        var result = ExperimentAnalyticsAggregator.Aggregate(experiment, exposures, customs, baselineVariantKey: "control");

        result.BaselineVariantKey.Should().Be("blue"); // first in ordinal order
    }

    private static Experiment NewExperiment(IReadOnlyList<string> metricKeys) => new()
    {
        Id = Guid.NewGuid(),
        Key = "exp",
        Name = "Exp",
        FlagKey = "flag",
        MetricKeys = [.. metricKeys],
        StartedAt = DateTimeOffset.UtcNow,
        EnvironmentId = Env,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Event Exposure(string subject, string variant, int atOffsetSeconds = 0) => new()
    {
        Id = Guid.NewGuid(),
        Type = EventType.Exposure,
        FlagKey = "flag",
        SubjectKey = subject,
        VariantKey = variant,
        At = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero).AddSeconds(atOffsetSeconds),
        EnvironmentId = Env,
    };

    private static Event Custom(string subject, string customKey) => new()
    {
        Id = Guid.NewGuid(),
        Type = EventType.Custom,
        CustomKey = customKey,
        SubjectKey = subject,
        At = new DateTimeOffset(2026, 5, 28, 1, 0, 0, TimeSpan.Zero),
        EnvironmentId = Env,
    };
}
