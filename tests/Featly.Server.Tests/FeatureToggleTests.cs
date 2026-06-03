using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
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
/// Covers the modular feature toggles (ADR-0024): the public meta endpoint
/// reflects the enabled set, defaults are everything-on, and a disabled area's
/// admin endpoints are simply not mapped (404 even with a valid admin key).
/// </summary>
public class FeatureToggleTests
{
    private const string AdminKey = "admin-key-test";

    [Fact]
    public async Task Meta_defaults_to_everything_on()
    {
        using var host = await BuildHostAsync();
        var meta = await host.GetTestClient().GetFromJsonAsync<JsonElement>("/api/meta", TestContext.Current.CancellationToken);

        var features = meta.GetProperty("features");
        foreach (var name in new[] { "flags", "configs", "segments", "experiments", "approvals", "webhooks", "audit", "rbac" })
        {
            features.GetProperty(name).GetBoolean().Should().BeTrue($"{name} should default to on");
        }
    }

    [Fact]
    public async Task Disabling_configs_unmaps_its_endpoints_but_keeps_flags()
    {
        using var host = await BuildHostAsync(o => o.Features.Configs = false);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        // Disabled area: route is gone -> 404 even with a valid admin key.
        (await client.GetAsync("/api/admin/configs", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Enabled area still served.
        (await client.GetAsync("/api/admin/flags", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await host.GetTestClient().GetFromJsonAsync<JsonElement>("/api/meta", ct);
        meta.GetProperty("features").GetProperty("configs").GetBoolean().Should().BeFalse();
        meta.GetProperty("features").GetProperty("flags").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Flags_only_deployment_unmaps_the_other_areas()
    {
        using var host = await BuildHostAsync(o =>
        {
            o.Features.Configs = false;
            o.Features.Segments = false;
            o.Features.Experiments = false;
            o.Features.Approvals = false;
            o.Features.Webhooks = false;
            o.Features.Audit = false;
            o.Features.Rbac = false;
        });
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync("/api/admin/flags", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var route in new[]
        {
            "/api/admin/configs", "/api/admin/segments", "/api/admin/experiments",
            "/api/admin/changes", "/api/admin/webhooks", "/api/admin/audit",
            "/api/admin/users", "/api/admin/roles", "/api/admin/groups",
        })
        {
            (await client.GetAsync(route, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"{route} should be unmapped");
        }

        // Core stays available regardless of toggles.
        (await client.GetAsync("/api/admin/environments", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<IHost> BuildHostAsync(Action<FeatlyServerOptions>? configure = null)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Featly:Server:AdminApiKey"] = AdminKey,
                }));
                web.ConfigureServices(services =>
                {
                    services.AddFeatlyInMemoryStore();
                    services.AddFeatlyServer(configure ?? (_ => { }));
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

        return await builder.StartAsync(TestContext.Current.CancellationToken);
    }
}
