using AwesomeAssertions;
using Featly.Server.Flags;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Unit tests for the pure <see cref="StaleFlagAnalyzer"/>: no-rules-and-stale,
/// stalled-experiment, and archived-but-referenced detection.
/// </summary>
public class StaleFlagAnalyzerTests
{
    private static readonly Guid Env = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    [Fact]
    public void Flag_with_no_rules_untouched_for_a_long_time_is_flagged()
    {
        var flag = NewFlag("kill-switch", updatedAt: Now.AddDays(-45));

        var result = StaleFlagAnalyzer.FindCandidates([flag], [], NoExposures, StaleAfter, Now);

        result.Should().ContainSingle(c => c.FlagKey == "kill-switch" && c.Reason == StaleFlagReason.NoTargetingRules);
    }

    [Fact]
    public void Flag_with_no_rules_but_recently_touched_is_not_flagged()
    {
        var flag = NewFlag("fresh", updatedAt: Now.AddDays(-5));

        var result = StaleFlagAnalyzer.FindCandidates([flag], [], NoExposures, StaleAfter, Now);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Flag_with_rules_is_never_flagged_for_NoTargetingRules_regardless_of_age()
    {
        var flag = NewFlag("targeted", updatedAt: Now.AddDays(-400));
        flag.Rules.Add(new Rule { Order = 0, Conditions = [], Outcome = new RuleOutcome { VariantKey = "on" } });

        var result = StaleFlagAnalyzer.FindCandidates([flag], [], NoExposures, StaleAfter, Now);

        result.Should().NotContain(c => c.Reason == StaleFlagReason.NoTargetingRules);
    }

    [Fact]
    public void Active_experiment_with_no_exposures_since_it_started_a_long_time_ago_is_flagged()
    {
        var flag = NewFlag("checkout", updatedAt: Now.AddDays(-1)); // recent edit shouldn't matter for this reason
        var experiment = NewExperiment("checkout-exp", "checkout", startedAt: Now.AddDays(-60));

        var result = StaleFlagAnalyzer.FindCandidates([flag], [experiment], NoExposures, StaleAfter, Now);

        result.Should().ContainSingle(c => c.FlagKey == "checkout" && c.Reason == StaleFlagReason.ExperimentStalledNoExposures);
    }

    [Fact]
    public void Active_experiment_with_stale_last_exposure_is_flagged()
    {
        var flag = NewFlag("checkout", updatedAt: Now.AddDays(-1));
        var experiment = NewExperiment("checkout-exp", "checkout", startedAt: Now.AddDays(-90));
        var lastExposure = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal) { ["checkout"] = Now.AddDays(-31) };

        var result = StaleFlagAnalyzer.FindCandidates([flag], [experiment], lastExposure, StaleAfter, Now);

        result.Should().ContainSingle(c => c.Reason == StaleFlagReason.ExperimentStalledNoExposures);
    }

    [Fact]
    public void Active_experiment_with_recent_exposures_is_not_flagged()
    {
        var flag = NewFlag("checkout", updatedAt: Now.AddDays(-1));
        var experiment = NewExperiment("checkout-exp", "checkout", startedAt: Now.AddDays(-90));
        var lastExposure = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal) { ["checkout"] = Now.AddDays(-2) };

        var result = StaleFlagAnalyzer.FindCandidates([flag], [experiment], lastExposure, StaleAfter, Now);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Stopped_experiment_never_triggers_the_stalled_reason()
    {
        var flag = NewFlag("checkout", updatedAt: Now.AddDays(-1));
        var experiment = NewExperiment("checkout-exp", "checkout", startedAt: Now.AddDays(-90), stoppedAt: Now.AddDays(-89));

        var result = StaleFlagAnalyzer.FindCandidates([flag], [experiment], NoExposures, StaleAfter, Now);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Archived_flag_with_still_active_experiment_is_flagged_and_skips_other_reasons()
    {
        var flag = NewFlag("old-checkout", updatedAt: Now.AddDays(-400));
        flag.Archived = true;
        var experiment = NewExperiment("old-checkout-exp", "old-checkout", startedAt: Now.AddDays(-5));

        var result = StaleFlagAnalyzer.FindCandidates([flag], [experiment], NoExposures, StaleAfter, Now);

        result.Should().ContainSingle();
        result[0].Reason.Should().Be(StaleFlagReason.ArchivedButExperimentStillActive);
    }

    [Fact]
    public void Archived_flag_with_no_active_experiment_is_never_flagged()
    {
        var flag = NewFlag("long-gone", updatedAt: Now.AddDays(-400));
        flag.Archived = true;

        var result = StaleFlagAnalyzer.FindCandidates([flag], [], NoExposures, StaleAfter, Now);

        result.Should().BeEmpty();
    }

    [Fact]
    public void A_flag_can_be_flagged_for_multiple_reasons_at_once()
    {
        var flag = NewFlag("checkout", updatedAt: Now.AddDays(-400)); // no rules, stale
        var experiment = NewExperiment("checkout-exp", "checkout", startedAt: Now.AddDays(-90));

        var result = StaleFlagAnalyzer.FindCandidates([flag], [experiment], NoExposures, StaleAfter, Now);

        result.Should().HaveCount(2);
        result.Select(c => c.Reason).Should().BeEquivalentTo(
            [StaleFlagReason.NoTargetingRules, StaleFlagReason.ExperimentStalledNoExposures]);
    }

    private static IReadOnlyDictionary<string, DateTimeOffset?> NoExposures { get; } =
        new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);

    private static Flag NewFlag(string key, DateTimeOffset updatedAt) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Type = FlagType.Boolean,
        Enabled = true,
        DefaultVariantKey = "off",
        Variants = [new Variant { Key = "off", Name = "Off", Value = System.Text.Json.JsonSerializer.SerializeToElement(false) }],
        EnvironmentId = Env,
        CreatedAt = updatedAt,
        UpdatedAt = updatedAt,
    };

    private static Experiment NewExperiment(string key, string flagKey, DateTimeOffset? startedAt, DateTimeOffset? stoppedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        FlagKey = flagKey,
        MetricKeys = [],
        StartedAt = startedAt,
        StoppedAt = stoppedAt,
        EnvironmentId = Env,
        CreatedAt = Now.AddDays(-100),
        UpdatedAt = Now.AddDays(-100),
    };
}
