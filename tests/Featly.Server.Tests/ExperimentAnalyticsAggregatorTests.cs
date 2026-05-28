using Featly.Server.Experiments;
using FluentAssertions;
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
