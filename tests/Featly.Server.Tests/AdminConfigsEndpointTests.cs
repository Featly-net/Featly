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
/// Exercises /api/admin/configs CRUD behind the admin auth policy. Mirrors
/// <see cref="AdminSegmentsEndpointTests"/>.
/// </summary>
public class AdminConfigsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task POST_creates_a_config_then_GET_returns_it()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var create = await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "checkout.timeout",
            name = "Checkout Timeout",
            description = "Seconds before checkout times out",
            type = "Int",
            defaultValue = 30,
        }, TestContext.Current.CancellationToken);

        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var get = await client.GetAsync(new Uri("/api/admin/configs/checkout.timeout", UriKind.Relative), TestContext.Current.CancellationToken);
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var loaded = await get.Content.ReadFromJsonAsync<Config>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        loaded.Should().NotBeNull();
        loaded!.Key.Should().Be("checkout.timeout");
        loaded.Type.Should().Be(ConfigType.Int);
        loaded.DefaultValue.GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task POST_returns_conflict_when_key_already_exists()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var payload = new
        {
            key = "dup",
            name = "Dup",
            type = "String",
            defaultValue = "x",
        };

        (await client.PostAsJsonAsync("/api/admin/configs", payload, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/admin/configs", payload, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PUT_updates_rules_and_default_value()
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

        var put = await client.PutAsJsonAsync("/api/admin/configs/checkout.timeout", new
        {
            key = "checkout.timeout",
            name = "Checkout Timeout (updated)",
            type = "Int",
            defaultValue = 45,
            rules = new[]
            {
                new
                {
                    order = 0,
                    name = "BR",
                    conditions = new[]
                    {
                        new { attribute = "user.country", @operator = "Equals", value = (object)"BR" },
                    },
                    value = (object)90,
                },
            },
        }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await put.Content.ReadFromJsonAsync<Config>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Checkout Timeout (updated)");
        updated.DefaultValue.GetInt32().Should().Be(45);
        updated.Rules.Should().ContainSingle();
        updated.Rules[0].Value.GetInt32().Should().Be(90);
    }

    [Fact]
    public async Task PUT_rejects_renaming_via_body_key()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "stay-the-same",
            name = "X",
            type = "String",
            defaultValue = "a",
        }, TestContext.Current.CancellationToken);

        var put = await client.PutAsJsonAsync("/api/admin/configs/stay-the-same", new
        {
            key = "different-key",
            name = "X",
            type = "String",
            defaultValue = "a",
        }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_returns_NotFound_when_config_missing()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var put = await client.PutAsJsonAsync("/api/admin/configs/ghost", new
        {
            key = "ghost",
            name = "G",
            type = "String",
            defaultValue = "a",
        }, TestContext.Current.CancellationToken);

        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_rejects_unauthenticated_requests()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "x",
            name = "X",
            type = "String",
            defaultValue = "a",
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_rejects_when_using_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "x",
            name = "X",
            type = "String",
            defaultValue = "a",
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GET_list_returns_all_configs_in_environment()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "a",
            name = "A",
            type = "Int",
            defaultValue = 1,
        }, TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "b",
            name = "B",
            type = "Int",
            defaultValue = 2,
        }, TestContext.Current.CancellationToken);

        var list = await client.GetAsync(new Uri("/api/admin/configs/", UriKind.Relative), TestContext.Current.CancellationToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var configs = await list.Content.ReadFromJsonAsync<List<Config>>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        configs.Should().NotBeNull();
        configs!.Select(c => c.Key).Should().BeEquivalentTo(["a", "b"]);
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
