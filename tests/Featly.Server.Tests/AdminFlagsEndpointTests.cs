using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Endpoints;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.Server.Tests;

public class AdminFlagsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task POST_admin_flags_rejects_unauthenticated_requests()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "demo",
            name = "Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = Array.Empty<object>(),
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_admin_flags_creates_a_flag_when_admin_key_is_supplied()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var response = await client.PostAsJsonAsync("/api/admin/flags", new
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
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var flag = await response.Content.ReadFromJsonAsync<Flag>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        flag.Should().NotBeNull();
        flag!.Key.Should().Be("demo");
        flag.Enabled.Should().BeTrue();
        flag.DefaultVariantKey.Should().Be("off");
        flag.Variants.Should().HaveCount(2);
    }

    [Fact]
    public async Task PUT_admin_flags_persists_rules_array()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        // Create without rules first.
        await client.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "rules-flag",
            name = "Rules flag",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
        }, TestContext.Current.CancellationToken);

        // PUT updates the flag with two rules: a deterministic match and a 50/50 split.
        var put = await client.PutAsJsonAsync("/api/admin/flags/rules-flag", new
        {
            key = "rules-flag",
            name = "Rules flag",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
            rules = new object[]
            {
                new
                {
                    order = 0,
                    name = "BR rollout",
                    enabled = true,
                    conditions = new[]
                    {
                        new { attribute = "user.country", @operator = "Equals", value = "BR" },
                    },
                    outcome = new { variantKey = "on" },
                },
                new
                {
                    order = 1,
                    name = "Enterprise split",
                    enabled = true,
                    conditions = new[]
                    {
                        new { attribute = "user.plan", @operator = "Equals", value = "enterprise" },
                    },
                    outcome = new
                    {
                        splits = new[]
                        {
                            new { variantKey = "on",  weight = 50 },
                            new { variantKey = "off", weight = 50 },
                        },
                    },
                },
            },
        }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var loaded = await put.Content.ReadFromJsonAsync<Flag>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        loaded.Should().NotBeNull();
        loaded!.Rules.Should().HaveCount(2);

        var brRule = loaded.Rules.Single(r => r.Order == 0);
        brRule.Name.Should().Be("BR rollout");
        brRule.Conditions.Should().ContainSingle().Which.Attribute.Should().Be("user.country");
        brRule.Outcome.VariantKey.Should().Be("on");

        var enterpriseRule = loaded.Rules.Single(r => r.Order == 1);
        enterpriseRule.Outcome.Splits.Should().HaveCount(2);
        enterpriseRule.Outcome.Splits!.Sum(s => s.Weight).Should().Be(100);
    }

    [Fact]
    public async Task Archive_then_unarchive_moves_flag_between_active_and_archived_lists()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "old-flag",
            name = "Old flag",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
        }, TestContext.Current.CancellationToken);

        var archive = await client.PostAsync(new Uri("/api/admin/flags/old-flag/archive", UriKind.Relative), null, TestContext.Current.CancellationToken);
        archive.StatusCode.Should().Be(HttpStatusCode.OK);
        var archived = await archive.Content.ReadFromJsonAsync<Flag>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        archived!.Archived.Should().BeTrue();

        var active = await client.GetFromJsonAsync<List<Flag>>(new Uri("/api/admin/flags", UriKind.Relative), TestJson.Options, TestContext.Current.CancellationToken);
        active!.Should().NotContain(f => f.Key == "old-flag");

        var archivedList = await client.GetFromJsonAsync<List<Flag>>(new Uri("/api/admin/flags?archived=true", UriKind.Relative), TestJson.Options, TestContext.Current.CancellationToken);
        archivedList!.Should().ContainSingle(f => f.Key == "old-flag");

        var restore = await client.PostAsync(new Uri("/api/admin/flags/old-flag/unarchive", UriKind.Relative), null, TestContext.Current.CancellationToken);
        restore.StatusCode.Should().Be(HttpStatusCode.OK);

        var activeAfter = await client.GetFromJsonAsync<List<Flag>>(new Uri("/api/admin/flags", UriKind.Relative), TestJson.Options, TestContext.Current.CancellationToken);
        activeAfter!.Should().ContainSingle(f => f.Key == "old-flag");
    }

    [Fact]
    public async Task Archive_returns_404_for_unknown_flag()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var archive = await client.PostAsync(new Uri("/api/admin/flags/ghost/archive", UriKind.Relative), null, TestContext.Current.CancellationToken);
        archive.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_admin_flags_rejects_when_using_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await client.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "demo",
            name = "Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = Array.Empty<object>(),
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
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
