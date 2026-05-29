using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Read-only listing endpoint used by the dashboard's environment selector.
/// </summary>
public class AdminEnvironmentsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task GET_admin_environments_rejects_unauthenticated_requests()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync(new Uri("/api/admin/environments", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_admin_environments_rejects_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await client.GetAsync(new Uri("/api/admin/environments", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GET_admin_environments_returns_the_bootstrap_default()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var response = await client.GetAsync(new Uri("/api/admin/environments", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var environments = await response.Content.ReadFromJsonAsync<List<Environment>>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        environments.Should().NotBeNull();
        environments!.Should().ContainSingle(e => e.IsDefault && e.Key == "development");
    }

    [Fact]
    public async Task Lock_then_unlock_toggles_readonly_and_gates_mutations()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        // Seed a flag while writable.
        (await client.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "ro-demo",
            name = "RO Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Lock the default environment.
        var lockResp = await client.PostAsync(new Uri("/api/admin/environments/development/lock", UriKind.Relative), null, ct);
        lockResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var locked = await lockResp.Content.ReadFromJsonAsync<Environment>(TestJson.Options, cancellationToken: ct);
        locked!.ReadOnly.Should().BeTrue();

        // A mutation is now rejected with 403.
        var blocked = await client.PutAsJsonAsync("/api/admin/flags/ro-demo", new
        {
            key = "ro-demo",
            name = "RO Demo edited",
            type = "Boolean",
            enabled = false,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct);
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Unlock restores writability.
        var unlockResp = await client.PostAsync(new Uri("/api/admin/environments/development/unlock", UriKind.Relative), null, ct);
        unlockResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await unlockResp.Content.ReadFromJsonAsync<Environment>(TestJson.Options, cancellationToken: ct))!.ReadOnly.Should().BeFalse();

        (await client.PutAsJsonAsync("/api/admin/flags/ro-demo", new
        {
            key = "ro-demo",
            name = "RO Demo edited",
            type = "Boolean",
            enabled = false,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The lock/unlock pair is audited.
        var audit = await client.GetFromJsonAsync<List<AuditEntry>>("/api/admin/audit?entityType=Environment", TestJson.Options, ct);
        audit!.Select(a => a.Action).Should().Contain([FeatlyEventTypes.EnvironmentLocked, FeatlyEventTypes.EnvironmentUnlocked]);
    }

    [Fact]
    public async Task Lock_returns_NotFound_for_unknown_environment()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);

        var resp = await client.PostAsync(new Uri("/api/admin/environments/ghost/lock", UriKind.Relative), null, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lock_rejects_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var resp = await client.PostAsync(new Uri("/api/admin/environments/development/lock", UriKind.Relative), null, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
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
