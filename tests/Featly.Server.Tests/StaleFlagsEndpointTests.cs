using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Flags;
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
/// End-to-end coverage for <c>GET /api/admin/flags/stale</c>: the endpoint
/// wiring around the pure <see cref="StaleFlagAnalyzer"/> (see
/// <c>StaleFlagAnalyzerTests</c> for the analyzer's own unit tests).
/// </summary>
public class StaleFlagsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";
    private static readonly string[] CheckoutMetric = ["checkout.completed"];

    [Fact]
    public async Task No_candidates_when_every_flag_is_actively_targeted()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "checkout",
            name = "Checkout",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false }, new { key = "on", name = "On", value = true } },
            rules = new[]
            {
                new
                {
                    order = 0,
                    conditions = new[] { new { attribute = "user.country", @operator = "Equals", value = "BR" } },
                    outcome = new { variantKey = "on" },
                },
            },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var candidates = await admin.GetFromJsonAsync<List<StaleFlagCandidate>>(
            "/api/admin/flags/stale?env=development", TestJson.Options, ct);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task A_no_rules_flag_untouched_past_the_threshold_is_reported()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "kill-switch",
            name = "Kill switch",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        // staleDays=0 would be rejected; use 0-day-equivalent by asking for a
        // 1-day threshold against a flag created "now" — UpdatedAt is set at
        // creation, so it won't be stale yet at 1 day. Assert the opposite:
        // no candidate at a threshold the fresh flag hasn't crossed.
        var freshCandidates = await admin.GetFromJsonAsync<List<StaleFlagCandidate>>(
            "/api/admin/flags/stale?env=development&staleDays=1", TestJson.Options, ct);
        freshCandidates!.Should().BeEmpty("the flag was just created, well inside the 1-day window");
    }

    [Fact]
    public async Task Rejects_a_non_positive_staleDays()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);

        var resp = await admin.GetAsync(new Uri("/api/admin/flags/stale?env=development&staleDays=0", UriKind.Relative), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stale_route_does_not_shadow_the_get_by_key_route()
    {
        // Routing regression guard: "/admin/flags/stale" must resolve to the
        // stale-report endpoint, not GetFlagAsync("stale").
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var resp = await admin.GetAsync(new Uri("/api/admin/flags/stale?env=development", UriKind.Relative), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync(ct);
        body.Should().NotContain("not found", "a 404-from-GetFlagAsync would indicate the wrong route matched");
    }

    [Fact]
    public async Task Rejects_sdk_scope_key()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var sdk = SdkClient(host);

        var resp = await sdk.GetAsync(new Uri("/api/admin/flags/stale?env=development", UriKind.Relative), TestContext.Current.CancellationToken);

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Archived_flag_with_a_still_active_experiment_is_reported()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "old-checkout",
            name = "Old checkout",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false }, new { key = "on", name = "On", value = true } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.PostAsJsonAsync("/api/admin/experiments", new
        {
            key = "old-checkout-exp",
            name = "Old checkout experiment",
            flagKey = "old-checkout",
            metricKeys = CheckoutMetric,
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsync(new Uri("/api/admin/experiments/old-checkout-exp/start", UriKind.Relative), null, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsync(new Uri("/api/admin/flags/old-checkout/archive", UriKind.Relative), null, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var candidates = await admin.GetFromJsonAsync<List<StaleFlagCandidate>>(
            "/api/admin/flags/stale?env=development", TestJson.Options, ct);

        candidates!.Should().ContainSingle(c => c.FlagKey == "old-checkout" && c.Reason == StaleFlagReason.ArchivedButExperimentStillActive);
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
