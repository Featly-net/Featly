using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Round-trips for the M9 experimentation stores: <c>Experiment</c> (with the
/// MetricKeys primitive collection and nullable started/stopped windows),
/// the append-only <c>Event</c> store (with a JsonElement properties bag), and
/// the first-write-wins <c>Assignment</c> store.
/// </summary>
public class SqliteExperimentStoreTests
{
    [Fact]
    public async Task Experiment_round_trips_metric_keys_window_and_flags()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var env = Guid.NewGuid();

        var experiment = new Experiment
        {
            Id = Guid.NewGuid(),
            Key = "checkout-color",
            Name = "Checkout button color",
            Hypothesis = "Green converts better than blue",
            FlagKey = "new-checkout",
            MetricKeys = ["checkout.completed", "checkout.started"],
            StickyAssignments = true,
            StartedAt = DateTimeOffset.UtcNow,
            StoppedAt = null,
            EnvironmentId = env,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await host.Store.Experiments.UpsertAsync(env, experiment, ct);

        var loaded = await host.Store.Experiments.GetByKeyAsync(env, "checkout-color", ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Checkout button color");
        loaded.Hypothesis.Should().Be("Green converts better than blue");
        loaded.FlagKey.Should().Be("new-checkout");
        loaded.MetricKeys.Should().BeEquivalentTo("checkout.completed", "checkout.started");
        loaded.StickyAssignments.Should().BeTrue();
        loaded.IsActive.Should().BeTrue();

        var byId = await host.Store.Experiments.GetByIdAsync(experiment.Id, ct);
        byId.Should().NotBeNull();
        byId!.Key.Should().Be("checkout-color");
    }

    [Fact]
    public async Task Upsert_updates_existing_experiment_in_place()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var env = Guid.NewGuid();

        var experiment = NewExperiment(env, "exp-1");
        await host.Store.Experiments.UpsertAsync(env, experiment, ct);

        experiment.Name = "Renamed";
        experiment.MetricKeys = ["only.one"];
        experiment.StoppedAt = DateTimeOffset.UtcNow;
        await host.Store.Experiments.UpsertAsync(env, experiment, ct);

        var all = await host.Store.Experiments.ListAsync(env, ct);
        all.Should().ContainSingle();
        all[0].Name.Should().Be("Renamed");
        all[0].MetricKeys.Should().ContainSingle().Which.Should().Be("only.one");
        all[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ListActive_returns_only_started_and_not_stopped()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var env = Guid.NewGuid();

        var draft = NewExperiment(env, "draft"); // StartedAt null
        var active = NewExperiment(env, "active");
        active.StartedAt = DateTimeOffset.UtcNow;
        var stopped = NewExperiment(env, "stopped");
        stopped.StartedAt = DateTimeOffset.UtcNow.AddDays(-1);
        stopped.StoppedAt = DateTimeOffset.UtcNow;

        await host.Store.Experiments.UpsertAsync(env, draft, ct);
        await host.Store.Experiments.UpsertAsync(env, active, ct);
        await host.Store.Experiments.UpsertAsync(env, stopped, ct);

        var activeList = await host.Store.Experiments.ListActiveAsync(env, ct);
        activeList.Should().ContainSingle().Which.Key.Should().Be("active");
    }

    [Fact]
    public async Task Experiments_are_scoped_per_environment_and_deletable()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        await host.Store.Experiments.UpsertAsync(envA, NewExperiment(envA, "shared"), ct);
        await host.Store.Experiments.UpsertAsync(envB, NewExperiment(envB, "shared"), ct);

        (await host.Store.Experiments.ListAsync(envA, ct)).Should().ContainSingle();
        (await host.Store.Experiments.ListAsync(envB, ct)).Should().ContainSingle();

        await host.Store.Experiments.DeleteAsync(envA, "shared", ct);
        (await host.Store.Experiments.GetByKeyAsync(envA, "shared", ct)).Should().BeNull();
        (await host.Store.Experiments.GetByKeyAsync(envB, "shared", ct)).Should().NotBeNull();

        // Idempotent: deleting a missing key is a no-op.
        await host.Store.Experiments.DeleteAsync(envA, "missing", ct);
    }

    [Fact]
    public async Task Events_append_and_query_filters_by_type_flag_and_custom_key()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var env = Guid.NewGuid();

        var exposure = new Event
        {
            Id = Guid.NewGuid(),
            Type = EventType.Exposure,
            FlagKey = "new-checkout",
            SubjectKey = "user-1",
            VariantKey = "green",
            At = DateTimeOffset.UtcNow,
            EnvironmentId = env,
        };
        var conversion = new Event
        {
            Id = Guid.NewGuid(),
            Type = EventType.Custom,
            CustomKey = "checkout.completed",
            SubjectKey = "user-1",
            Properties = new Dictionary<string, JsonElement>
            {
                ["revenue"] = JsonDocument.Parse("42.5").RootElement,
                ["plan"] = JsonDocument.Parse("\"pro\"").RootElement,
            },
            At = DateTimeOffset.UtcNow.AddSeconds(5),
            EnvironmentId = env,
        };
        var otherEnv = new Event
        {
            Id = Guid.NewGuid(),
            Type = EventType.Exposure,
            FlagKey = "new-checkout",
            SubjectKey = "user-9",
            At = DateTimeOffset.UtcNow,
            EnvironmentId = Guid.NewGuid(),
        };
        await host.Store.Events.AppendAsync([exposure, conversion, otherEnv], ct);

        var all = await host.Store.Events.QueryAsync(env, ct: ct);
        all.Should().HaveCount(2);

        var exposures = await host.Store.Events.QueryAsync(env, type: EventType.Exposure, ct: ct);
        exposures.Should().ContainSingle().Which.VariantKey.Should().Be("green");

        var byFlag = await host.Store.Events.QueryAsync(env, flagKey: "new-checkout", ct: ct);
        byFlag.Should().ContainSingle();

        var conversions = await host.Store.Events.QueryAsync(env, customKey: "checkout.completed", ct: ct);
        conversions.Should().ContainSingle();
        var props = conversions[0].Properties;
        props.Should().NotBeNull();
        props!["revenue"].GetDouble().Should().Be(42.5);
        props["plan"].GetString().Should().Be("pro");
    }

    [Fact]
    public async Task Assignment_is_first_write_wins_per_subject()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var experimentId = Guid.NewGuid();

        await host.Store.Assignments.UpsertAsync(new Assignment
        {
            Id = Guid.NewGuid(),
            ExperimentId = experimentId,
            SubjectKey = "user-1",
            VariantKey = "green",
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        // Second write for the same subject must not change the variant.
        await host.Store.Assignments.UpsertAsync(new Assignment
        {
            Id = Guid.NewGuid(),
            ExperimentId = experimentId,
            SubjectKey = "user-1",
            VariantKey = "blue",
            AssignedAt = DateTimeOffset.UtcNow.AddMinutes(1),
        }, ct);

        var loaded = await host.Store.Assignments.GetAsync(experimentId, "user-1", ct);
        loaded.Should().NotBeNull();
        loaded!.VariantKey.Should().Be("green");

        await host.Store.Assignments.UpsertAsync(new Assignment
        {
            Id = Guid.NewGuid(),
            ExperimentId = experimentId,
            SubjectKey = "user-2",
            VariantKey = "blue",
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var list = await host.Store.Assignments.ListByExperimentAsync(experimentId, ct);
        list.Should().HaveCount(2);
    }

    private static Experiment NewExperiment(Guid env, string key) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        FlagKey = "some-flag",
        MetricKeys = ["m1"],
        EnvironmentId = env,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
