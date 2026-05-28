using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Featly.Server;
using Featly.Server.Experiments;
using Featly.Storage.InMemory;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Exercises /api/admin/experiments CRUD + start/stop, /api/sdk/events ingest,
/// and the analytics endpoint end-to-end behind the auth policies.
/// </summary>
public class AdminExperimentsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";
    private static readonly string[] CheckoutMetric = ["checkout.completed"];

    [Fact]
    public async Task POST_creates_experiment_then_GET_returns_it()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);

        await SeedFlagAsync(admin);

        var create = await admin.PostAsJsonAsync("/api/admin/experiments", new
        {
            key = "checkout-color",
            name = "Checkout color",
            hypothesis = "green wins",
            flagKey = "checkout",
            metricKeys = CheckoutMetric,
            stickyAssignments = true,
        }, TestContext.Current.CancellationToken);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var get = await admin.GetAsync(new Uri("/api/admin/experiments/checkout-color", UriKind.Relative), TestContext.Current.CancellationToken);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await get.Content.ReadFromJsonAsync<Experiment>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        loaded.Should().NotBeNull();
        loaded!.FlagKey.Should().Be("checkout");
        loaded.MetricKeys.Should().ContainSingle().Which.Should().Be("checkout.completed");
        loaded.StickyAssignments.Should().BeTrue();
        loaded.IsActive.Should().BeFalse(); // draft until started
    }

    [Fact]
    public async Task POST_rejects_experiment_for_missing_flag()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);

        var create = await admin.PostAsJsonAsync("/api/admin/experiments", new
        {
            key = "x",
            name = "X",
            flagKey = "does-not-exist",
        }, TestContext.Current.CancellationToken);

        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Start_then_stop_drives_the_window_and_active_flag()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        await SeedFlagAsync(admin);
        await CreateExperimentAsync(admin);

        var start = await admin.PostAsync(new Uri("/api/admin/experiments/exp/start", UriKind.Relative), null, TestContext.Current.CancellationToken);
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await start.Content.ReadFromJsonAsync<Experiment>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        started!.IsActive.Should().BeTrue();

        // Starting an already-running experiment conflicts.
        (await admin.PostAsync(new Uri("/api/admin/experiments/exp/start", UriKind.Relative), null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var stop = await admin.PostAsync(new Uri("/api/admin/experiments/exp/stop", UriKind.Relative), null, TestContext.Current.CancellationToken);
        stop.StatusCode.Should().Be(HttpStatusCode.OK);
        var stopped = await stop.Content.ReadFromJsonAsync<Experiment>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        stopped!.IsActive.Should().BeFalse();
        stopped.StoppedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Stop_before_start_conflicts()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        await SeedFlagAsync(admin);
        await CreateExperimentAsync(admin);

        (await admin.PostAsync(new Uri("/api/admin/experiments/exp/stop", UriKind.Relative), null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Active_experiment_ships_in_the_sdk_snapshot()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var sdk = SdkClient(host);
        await SeedFlagAsync(admin);
        await CreateExperimentAsync(admin);

        // Draft: not in snapshot.
        var before = await sdk.GetFromJsonAsync<ConfigSnapshot>("/api/sdk/config", TestJson.Options, TestContext.Current.CancellationToken);
        (before!.Experiments ?? []).Should().BeEmpty();

        await admin.PostAsync(new Uri("/api/admin/experiments/exp/start", UriKind.Relative), null, TestContext.Current.CancellationToken);

        var after = await sdk.GetFromJsonAsync<ConfigSnapshot>("/api/sdk/config", TestJson.Options, TestContext.Current.CancellationToken);
        (after!.Experiments ?? []).Should().ContainSingle().Which.Key.Should().Be("exp");
    }

    [Fact]
    public async Task Events_ingest_then_analytics_reports_conversion_rates()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var sdk = SdkClient(host);
        await SeedFlagAsync(admin);
        await CreateExperimentAsync(admin, metricKeys: ["checkout.completed"]);
        await admin.PostAsync(new Uri("/api/admin/experiments/exp/start", UriKind.Relative), null, TestContext.Current.CancellationToken);

        var ingest = await sdk.PostAsJsonAsync("/api/sdk/events", new
        {
            events = new object[]
            {
                new { type = "Exposure", subjectKey = "g1", flagKey = "checkout", variantKey = "green" },
                new { type = "Exposure", subjectKey = "g2", flagKey = "checkout", variantKey = "green" },
                new { type = "Exposure", subjectKey = "b1", flagKey = "checkout", variantKey = "blue" },
                new { type = "Custom", subjectKey = "g1", customKey = "checkout.completed" },
                new { type = "Custom", subjectKey = "b1", customKey = "checkout.completed" },
            },
        }, TestContext.Current.CancellationToken);
        ingest.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var analytics = await admin.GetFromJsonAsync<ExperimentAnalytics>(
            "/api/admin/experiments/exp/analytics", TestJson.Options, TestContext.Current.CancellationToken);

        analytics.Should().NotBeNull();
        analytics!.TotalExposedSubjects.Should().Be(3);
        var green = analytics.Variants.Single(v => v.VariantKey == "green");
        green.ExposedSubjects.Should().Be(2);
        green.Metrics.Single().Conversions.Should().Be(1);
        green.Metrics.Single().ConversionRate.Should().Be(0.5);
        var blue = analytics.Variants.Single(v => v.VariantKey == "blue");
        blue.Metrics.Single().ConversionRate.Should().Be(1.0);
    }

    [Fact]
    public async Task Events_ingest_rejects_admin_scope_key()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);

        var response = await admin.PostAsJsonAsync("/api/sdk/events", new
        {
            events = new object[] { new { type = "Custom", subjectKey = "s", customKey = "k" } },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task POST_experiments_rejects_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var sdk = SdkClient(host);

        var response = await sdk.PostAsJsonAsync("/api/admin/experiments", new
        {
            key = "x",
            name = "X",
            flagKey = "checkout",
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    private static HttpClient SdkClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);
        return client;
    }

    private static async Task SeedFlagAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "checkout",
            name = "Checkout",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
        }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task CreateExperimentAsync(HttpClient admin, IReadOnlyList<string>? metricKeys = null)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/experiments", new
        {
            key = "exp",
            name = "Exp",
            flagKey = "checkout",
            metricKeys = metricKeys ?? CheckoutMetric,
        }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Featly:Server:AdminApiKey"] = AdminKey,
                    ["Featly:Server:SdkApiKey"] = SdkKey,
                }));
                web.ConfigureServices(services =>
                {
                    services.AddFeatlyInMemoryStore();
                    services.AddFeatlyServer();
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapFeatlyApi());
                });
            });

        var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }
}
