using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Endpoints;
using Featly.Server.Telemetry;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// In-process SDK activity: config-sync timestamps and active SSE connection
/// counts per environment (<c>GET /api/admin/environments/{key}/sdk-activity</c>),
/// and exposure-derived flag activity (<c>GET /api/admin/flags/{key}/activity</c>).
/// Neither touches the SDK's local evaluation hot path — both observe calls
/// that are already a network round-trip by design.
/// </summary>
public class SdkActivityTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task Sdk_activity_is_empty_before_any_client_connects()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var activity = await admin.GetFromJsonAsync<SdkActivitySnapshot>(
            "/api/admin/environments/development/sdk-activity", TestJson.Options, ct);

        activity!.ActiveStreamConnections.Should().Be(0);
        activity.LastConfigSyncAt.Should().BeNull();
        activity.LastStreamConnectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Config_sync_records_the_timestamp_without_opening_a_connection()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var sdk = SdkClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await sdk.GetAsync(new Uri("/api/sdk/config", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var activity = await admin.GetFromJsonAsync<SdkActivitySnapshot>(
            "/api/admin/environments/development/sdk-activity", TestJson.Options, ct);

        activity!.LastConfigSyncAt.Should().NotBeNull();
        activity.ActiveStreamConnections.Should().Be(0, "a config poll is not a stream connection");
    }

    [Fact]
    public async Task Sdk_activity_rejects_sdk_scope_key()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var sdk = SdkClient(host);

        var resp = await sdk.GetAsync(new Uri("/api/admin/environments/development/sdk-activity", UriKind.Relative), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sdk_activity_returns_NotFound_for_unknown_environment()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);

        var resp = await admin.GetAsync(new Uri("/api/admin/environments/ghost/sdk-activity", UriKind.Relative), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Flag_activity_is_null_for_a_flag_with_no_experiment_history()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "never-experimented",
            name = "Never experimented",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var activity = await admin.GetFromJsonAsync<FlagActivityView>(
            "/api/admin/flags/never-experimented/activity", TestJson.Options, ct);

        activity!.LastExposureAt.Should().BeNull();
        activity.TotalExposureEvents.Should().Be(0);
    }

    [Fact]
    public async Task Flag_activity_reports_the_most_recent_exposure()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var sdk = SdkClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await admin.PostAsJsonAsync("/api/admin/flags", new
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
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var ingest = await sdk.PostAsJsonAsync("/api/sdk/events", new
        {
            events = new object[]
            {
                new { type = "Exposure", subjectKey = "s1", flagKey = "checkout", variantKey = "on" },
                new { type = "Exposure", subjectKey = "s2", flagKey = "checkout", variantKey = "off" },
            },
        }, ct);
        ingest.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var activity = await admin.GetFromJsonAsync<FlagActivityView>(
            "/api/admin/flags/checkout/activity", TestJson.Options, ct);

        activity!.LastExposureAt.Should().NotBeNull();
        activity.TotalExposureEvents.Should().Be(2);
    }

    [Fact]
    public async Task Stream_opens_and_emits_the_hello_frame()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var sdk = SdkClient(host);

        // The SSE stream is long-lived, so cap the read: we only need to confirm
        // it opens (channel created) and emits the initial hello frame.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        using var response = await sdk.GetAsync(
            new Uri("/api/sdk/stream", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var buffer = new char[128];
        var read = await reader.ReadAsync(buffer.AsMemory(), cts.Token);
        new string(buffer, 0, read).Should().Contain("hello");
    }

    [Fact]
    public async Task Events_ingest_rejects_a_batch_over_the_cap()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var sdk = SdkClient(host);
        var ct = TestContext.Current.CancellationToken;

        // Default MaxEventBatchSize is 1000; a 1001-event batch is refused before
        // any storage work (issue #204).
        var events = Enumerable.Range(0, 1001)
            .Select(i => new { type = "Custom", subjectKey = "s" + i, customKey = "evt" })
            .ToArray();

        var ingest = await sdk.PostAsJsonAsync("/api/sdk/events", new { events }, ct);

        ingest.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
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

}
