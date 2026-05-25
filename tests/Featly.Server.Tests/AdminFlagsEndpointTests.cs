using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Featly.Server;
using Featly.Server.Endpoints;
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
