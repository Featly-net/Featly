using System.Collections.Concurrent;
using System.Text.Json;
using AwesomeAssertions;
using Featly.Sdk.Internal;
using Xunit;

namespace Featly.Sdk.Tests;

/// <summary>
/// Covers the M9 SDK telemetry surface: <see cref="EventClient"/> custom-event
/// tracking and the automatic exposure emission + sticky pinning that
/// <see cref="FlagClient"/> performs through <see cref="ExperimentExposureProcessor"/>.
/// </summary>
public class EventClientAndExposureTests
{
    private sealed class CapturingSink : IEventSink
    {
        public ConcurrentQueue<QueuedEvent> Events { get; } = new();

        public void Enqueue(QueuedEvent evt) => Events.Enqueue(evt);
    }

    private sealed class FixedAccessor(EvaluationContext? context) : IFeatlyContextAccessor
    {
        public EvaluationContext? Current { get; } = context;
    }

    private static readonly Guid Env = Guid.NewGuid();

    // ---- EventClient.TrackAsync ------------------------------------------------

    [Fact]
    public async Task TrackAsync_enqueues_custom_event_with_subject_and_properties()
    {
        var sink = new CapturingSink();
        var client = new EventClient(sink, new FixedAccessor(new EvaluationContext("user-1")));

        await client.TrackAsync("checkout.completed", new { revenue = 42.5, plan = "pro" }, ct: TestContext.Current.CancellationToken);

        sink.Events.Should().ContainSingle();
        sink.Events.TryDequeue(out var evt).Should().BeTrue();
        evt!.Type.Should().Be(EventType.Custom);
        evt.CustomKey.Should().Be("checkout.completed");
        evt.SubjectKey.Should().Be("user-1");
        evt.Properties.Should().NotBeNull();
        evt.Properties!["revenue"].GetDouble().Should().Be(42.5);
        evt.Properties["plan"].GetString().Should().Be("pro");
    }

    [Fact]
    public async Task TrackAsync_uses_explicit_context_over_ambient()
    {
        var sink = new CapturingSink();
        var client = new EventClient(sink, new FixedAccessor(new EvaluationContext("ambient")));

        await client.TrackAsync("evt", context: new EvaluationContext("explicit"), ct: TestContext.Current.CancellationToken);

        sink.Events.TryDequeue(out var evt).Should().BeTrue();
        evt!.SubjectKey.Should().Be("explicit");
    }

    [Fact]
    public async Task TrackAsync_drops_event_without_a_subject()
    {
        var sink = new CapturingSink();
        var client = new EventClient(sink, new FixedAccessor(null));

        await client.TrackAsync("evt", ct: TestContext.Current.CancellationToken);

        sink.Events.Should().BeEmpty();
    }

    // ---- Automatic exposure emission ------------------------------------------

    [Fact]
    public async Task Evaluating_a_flag_under_an_active_experiment_emits_one_exposure()
    {
        var sink = new CapturingSink();
        var cache = new FeatlySnapshotCache();
        cache.Replace(Snapshot(experiment: ActiveExperiment(sticky: false)), etag: null);
        var client = new FlagClient(cache, new FixedAccessor(new EvaluationContext("user-1")), new ExperimentExposureProcessor(sink));

        await client.GetVariantAsync("demo", "off", ct: TestContext.Current.CancellationToken);
        await client.GetVariantAsync("demo", "off", ct: TestContext.Current.CancellationToken); // re-eval: deduped

        sink.Events.Should().ContainSingle();
        sink.Events.TryDequeue(out var evt).Should().BeTrue();
        evt!.Type.Should().Be(EventType.Exposure);
        evt.FlagKey.Should().Be("demo");
        evt.SubjectKey.Should().Be("user-1");
        evt.VariantKey.Should().Be("on");
    }

    [Fact]
    public async Task No_exposure_when_no_experiment_covers_the_flag()
    {
        var sink = new CapturingSink();
        var cache = new FeatlySnapshotCache();
        cache.Replace(Snapshot(experiment: null), etag: null);
        var client = new FlagClient(cache, new FixedAccessor(new EvaluationContext("user-1")), new ExperimentExposureProcessor(sink));

        await client.GetVariantAsync("demo", "off", ct: TestContext.Current.CancellationToken);

        sink.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task No_exposure_without_a_subject_key()
    {
        var sink = new CapturingSink();
        var cache = new FeatlySnapshotCache();
        cache.Replace(Snapshot(experiment: ActiveExperiment(sticky: false)), etag: null);
        var client = new FlagClient(cache, new FixedAccessor(null), new ExperimentExposureProcessor(sink));

        await client.GetVariantAsync("demo", "off", ct: TestContext.Current.CancellationToken);

        sink.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Distinct_subjects_each_get_one_exposure()
    {
        var sink = new CapturingSink();
        var cache = new FeatlySnapshotCache();
        cache.Replace(Snapshot(experiment: ActiveExperiment(sticky: false)), etag: null);
        var processor = new ExperimentExposureProcessor(sink);

        var a = new FlagClient(cache, new FixedAccessor(new EvaluationContext("a")), processor);
        var b = new FlagClient(cache, new FixedAccessor(new EvaluationContext("b")), processor);
        await a.GetVariantAsync("demo", "off", ct: TestContext.Current.CancellationToken);
        await b.GetVariantAsync("demo", "off", ct: TestContext.Current.CancellationToken);

        sink.Events.Should().HaveCount(2);
    }

    // ---- Sticky pinning --------------------------------------------------------

    [Fact]
    public void Sticky_experiment_pins_subject_to_first_variant_after_weight_change()
    {
        var sink = new CapturingSink();
        var processor = new ExperimentExposureProcessor(sink);
        var experiment = ActiveExperiment(sticky: true);

        // First evaluation buckets the subject into "on".
        processor.Process(experiment, "user-1", "on").Should().Be("on");
        // A later weight change would bucket the same subject into "off" — sticky pins "on".
        processor.Process(experiment, "user-1", "off").Should().Be("on");
        // A different subject is bucketed fresh.
        processor.Process(experiment, "user-2", "off").Should().Be("off");

        // Only one exposure per distinct subject.
        sink.Events.Should().HaveCount(2);
    }

    [Fact]
    public void NonSticky_experiment_does_not_pin()
    {
        var sink = new CapturingSink();
        var processor = new ExperimentExposureProcessor(sink);
        var experiment = ActiveExperiment(sticky: false);

        processor.Process(experiment, "user-1", "on").Should().Be("on");
        processor.Process(experiment, "user-1", "off").Should().Be("off"); // honours the fresh bucket
    }

    private static Experiment ActiveExperiment(bool sticky) => new()
    {
        Id = Guid.NewGuid(),
        Key = "exp",
        Name = "Exp",
        FlagKey = "demo",
        MetricKeys = ["m"],
        StickyAssignments = sticky,
        StartedAt = DateTimeOffset.UtcNow,
        EnvironmentId = Env,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static ConfigSnapshot Snapshot(Experiment? experiment)
    {
        var flag = new Flag
        {
            Id = Guid.NewGuid(),
            Key = "demo",
            Name = "Demo",
            Type = FlagType.Boolean,
            Enabled = true,
            DefaultVariantKey = "on",
            EnvironmentId = Env,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Variants =
            [
                new Variant { Key = "on", Name = "On", Value = JsonSerializer.SerializeToElement("on") },
                new Variant { Key = "off", Name = "Off", Value = JsonSerializer.SerializeToElement("off") },
            ],
        };

        return new ConfigSnapshot(
            EnvironmentId: Env,
            EnvironmentKey: "development",
            At: DateTimeOffset.UtcNow,
            Flags: [flag],
            Segments: [],
            Configs: [],
            Experiments: experiment is null ? [] : [experiment]);
    }
}
