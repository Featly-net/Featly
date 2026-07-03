using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trips for the experimentation stores, mirroring
/// <c>SqliteExperimentStoreTests</c>: <c>PostgresExperimentStore</c> (with the
/// MetricKeys native jsonb array and nullable started/stopped window), the
/// append-only <c>PostgresEventStore</c> (with a JsonElement properties bag),
/// and the first-write-wins <c>PostgresAssignmentStore</c>.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresExperimentStoreTests
{
    [Fact]
    public async Task Experiment_round_trips_metric_keys_window_and_flags()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ExperimentStore;
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
        await store.UpsertAsync(env, experiment, ct);

        var loaded = await store.GetByKeyAsync(env, "checkout-color", ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Checkout button color");
        loaded.Hypothesis.Should().Be("Green converts better than blue");
        loaded.FlagKey.Should().Be("new-checkout");
        loaded.MetricKeys.Should().BeEquivalentTo("checkout.completed", "checkout.started");
        loaded.StickyAssignments.Should().BeTrue();
        loaded.IsActive.Should().BeTrue();

        var byId = await store.GetByIdAsync(experiment.Id, ct);
        byId.Should().NotBeNull();
        byId!.Key.Should().Be("checkout-color");
    }

    [Fact]
    public async Task Upsert_updates_existing_experiment_in_place()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ExperimentStore;
        var env = Guid.NewGuid();

        var experiment = NewExperiment(env, "exp-1");
        await store.UpsertAsync(env, experiment, ct);

        experiment.Name = "Renamed";
        experiment.MetricKeys = ["only.one"];
        experiment.StoppedAt = DateTimeOffset.UtcNow;
        await store.UpsertAsync(env, experiment, ct);

        var all = await store.ListAsync(env, ct);
        all.Should().ContainSingle();
        all[0].Name.Should().Be("Renamed");
        all[0].MetricKeys.Should().ContainSingle().Which.Should().Be("only.one");
        all[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ListActive_returns_only_started_and_not_stopped()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ExperimentStore;
        var env = Guid.NewGuid();

        var draft = NewExperiment(env, "draft"); // StartedAt null
        var active = NewExperiment(env, "active");
        active.StartedAt = DateTimeOffset.UtcNow;
        var stopped = NewExperiment(env, "stopped");
        stopped.StartedAt = DateTimeOffset.UtcNow.AddDays(-1);
        stopped.StoppedAt = DateTimeOffset.UtcNow;

        await store.UpsertAsync(env, draft, ct);
        await store.UpsertAsync(env, active, ct);
        await store.UpsertAsync(env, stopped, ct);

        var activeList = await store.ListActiveAsync(env, ct);
        activeList.Should().ContainSingle().Which.Key.Should().Be("active");
    }

    [Fact]
    public async Task Experiments_are_scoped_per_environment_and_deletable()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ExperimentStore;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        await store.UpsertAsync(envA, NewExperiment(envA, "shared"), ct);
        await store.UpsertAsync(envB, NewExperiment(envB, "shared"), ct);

        (await store.ListAsync(envA, ct)).Should().ContainSingle();
        (await store.ListAsync(envB, ct)).Should().ContainSingle();

        await store.DeleteAsync(envA, "shared", ct);
        (await store.GetByKeyAsync(envA, "shared", ct)).Should().BeNull();
        (await store.GetByKeyAsync(envB, "shared", ct)).Should().NotBeNull();

        // Idempotent: deleting a missing key is a no-op.
        await store.DeleteAsync(envA, "missing", ct);
    }

    [Fact]
    public async Task Events_append_and_query_filters_by_type_flag_and_custom_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.EventStore;
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
        await store.AppendAsync([exposure, conversion, otherEnv], ct);

        var all = await store.QueryAsync(env, ct: ct);
        all.Should().HaveCount(2);

        var exposures = await store.QueryAsync(env, type: EventType.Exposure, ct: ct);
        exposures.Should().ContainSingle().Which.VariantKey.Should().Be("green");

        var byFlag = await store.QueryAsync(env, flagKey: "new-checkout", ct: ct);
        byFlag.Should().ContainSingle();

        var conversions = await store.QueryAsync(env, customKey: "checkout.completed", ct: ct);
        conversions.Should().ContainSingle();
        var props = conversions[0].Properties;
        props.Should().NotBeNull();
        props!["revenue"].GetDouble().Should().Be(42.5);
        props["plan"].GetString().Should().Be("pro");
    }

    [Fact]
    public async Task Assignment_is_first_write_wins_per_subject()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.AssignmentStore;
        var experimentId = Guid.NewGuid();

        await store.UpsertAsync(new Assignment
        {
            Id = Guid.NewGuid(),
            ExperimentId = experimentId,
            SubjectKey = "user-1",
            VariantKey = "green",
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        // Second write for the same subject must not change the variant.
        await store.UpsertAsync(new Assignment
        {
            Id = Guid.NewGuid(),
            ExperimentId = experimentId,
            SubjectKey = "user-1",
            VariantKey = "blue",
            AssignedAt = DateTimeOffset.UtcNow.AddMinutes(1),
        }, ct);

        var loaded = await store.GetAsync(experimentId, "user-1", ct);
        loaded.Should().NotBeNull();
        loaded!.VariantKey.Should().Be("green");

        await store.UpsertAsync(new Assignment
        {
            Id = Guid.NewGuid(),
            ExperimentId = experimentId,
            SubjectKey = "user-2",
            VariantKey = "blue",
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var list = await store.ListByExperimentAsync(experimentId, ct);
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
