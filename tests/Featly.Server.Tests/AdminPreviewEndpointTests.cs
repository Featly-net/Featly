using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Featly.Server;
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
/// Covers the dashboard "test this context" preview endpoints — POST
/// /api/admin/preview/flags/{key} and /configs/{key}. The endpoints reuse
/// the shared Evaluator and load segments from the same environment so
/// InSegment conditions resolve correctly.
/// </summary>
public class AdminPreviewEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task POST_preview_flag_rejects_unauthenticated_requests()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/admin/preview/flags/demo", new
        {
            targetingKey = "alice",
            attributes = new Dictionary<string, object?>(),
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_preview_flag_rejects_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await client.PostAsJsonAsync("/api/admin/preview/flags/demo", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task POST_preview_flag_returns_default_when_no_rules_match()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await CreateBrTargetedFlagAsync(client);

        var response = await client.PostAsJsonAsync("/api/admin/preview/flags/demo", new
        {
            attributes = new Dictionary<string, object?> { ["user.country"] = "US" },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        result.GetProperty("reason").GetString().Should().Be("Default");
        result.GetProperty("variantKey").GetString().Should().Be("off");
        result.GetProperty("value").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task POST_preview_flag_returns_targeting_match_when_rule_fires()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await CreateBrTargetedFlagAsync(client);

        var response = await client.PostAsJsonAsync("/api/admin/preview/flags/demo", new
        {
            attributes = new Dictionary<string, object?> { ["user.country"] = "BR" },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        result.GetProperty("reason").GetString().Should().Be("TargetingMatch");
        result.GetProperty("variantKey").GetString().Should().Be("on");
        result.GetProperty("ruleMatched").GetString().Should().Be("BR");
        result.GetProperty("value").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task POST_preview_flag_resolves_in_segment_conditions()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        // Segment that matches user.plan = enterprise.
        await client.PostAsJsonAsync("/api/admin/segments", new
        {
            key = "enterprise",
            name = "Enterprise",
            conditions = new[]
            {
                new { attribute = "user.plan", @operator = "Equals", value = "enterprise" },
            },
        }, TestContext.Current.CancellationToken);

        // Flag with a rule that uses InSegment.
        await client.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "demo",
            name = "Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
            rules = new[]
            {
                new
                {
                    order = 0,
                    name = "enterprise-only",
                    enabled = true,
                    conditions = new[]
                    {
                        new { attribute = "ignored", @operator = "InSegment", value = "enterprise" },
                    },
                    outcome = new { variantKey = "on" },
                },
            },
        }, TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/admin/preview/flags/demo", new
        {
            attributes = new Dictionary<string, object?> { ["user.plan"] = "enterprise" },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        result.GetProperty("reason").GetString().Should().Be("TargetingMatch");
        result.GetProperty("variantKey").GetString().Should().Be("on");
    }

    [Fact]
    public async Task POST_preview_flag_returns_404_when_flag_missing()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var response = await client.PostAsJsonAsync("/api/admin/preview/flags/ghost", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_preview_config_returns_rule_value_on_match()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "checkout.timeout",
            name = "Checkout Timeout",
            type = "Int",
            defaultValue = 30,
            rules = new[]
            {
                new
                {
                    order = 0,
                    name = "BR",
                    enabled = true,
                    conditions = new[]
                    {
                        new { attribute = "user.country", @operator = "Equals", value = "BR" },
                    },
                    value = (object)60,
                },
            },
        }, TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/admin/preview/configs/checkout.timeout", new
        {
            attributes = new Dictionary<string, object?> { ["user.country"] = "BR" },
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        result.GetProperty("reason").GetString().Should().Be("TargetingMatch");
        result.GetProperty("ruleMatched").GetString().Should().Be("BR");
        result.GetProperty("value").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task POST_preview_config_returns_default_when_no_rule_matches()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "checkout.timeout",
            name = "Checkout Timeout",
            type = "Int",
            defaultValue = 30,
        }, TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync("/api/admin/preview/configs/checkout.timeout", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        result.GetProperty("reason").GetString().Should().Be("Default");
        result.GetProperty("value").GetInt32().Should().Be(30);
    }

    private static async Task CreateBrTargetedFlagAsync(HttpClient client)
    {
        await client.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "demo",
            name = "Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
            rules = new[]
            {
                new
                {
                    order = 0,
                    name = "BR",
                    enabled = true,
                    conditions = new[]
                    {
                        new { attribute = "user.country", @operator = "Equals", value = "BR" },
                    },
                    outcome = new { variantKey = "on" },
                },
            },
        });
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
