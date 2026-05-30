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

    [Fact]
    public async Task Create_then_list_includes_the_new_environment()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var created = await client.PostAsJsonAsync("/api/admin/environments", new { key = "staging", name = "Staging" }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var envs = await client.GetFromJsonAsync<List<Environment>>("/api/admin/environments", TestJson.Options, ct);
        envs!.Should().Contain(e => e.Key == "staging" && e.Name == "Staging" && !e.IsDefault);
    }

    [Fact]
    public async Task Create_with_duplicate_key_returns_Conflict()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await client.PostAsJsonAsync("/api/admin/environments", new { key = "staging", name = "S1" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/admin/environments", new { key = "staging", name = "S2" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rename_changes_the_name_and_keeps_the_key()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await client.PostAsJsonAsync("/api/admin/environments", new { key = "staging", name = "Staging" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var renamed = await client.PutAsJsonAsync("/api/admin/environments/staging", new { key = "staging", name = "Staging Env" }, ct);
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);

        var envs = await client.GetFromJsonAsync<List<Environment>>("/api/admin/environments", TestJson.Options, ct);
        envs!.Single(e => e.Key == "staging").Name.Should().Be("Staging Env");
    }

    [Fact]
    public async Task Update_unknown_environment_returns_NotFound()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);

        var resp = await client.PutAsJsonAsync("/api/admin/environments/ghost", new { key = "ghost", name = "Ghost" }, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_empty_env_succeeds_and_default_is_protected()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await client.PostAsJsonAsync("/api/admin/environments", new { key = "staging", name = "Staging" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.DeleteAsync(new Uri("/api/admin/environments/staging", UriKind.Relative), ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var envs = await client.GetFromJsonAsync<List<Environment>>("/api/admin/environments", TestJson.Options, ct);
        envs!.Should().NotContain(e => e.Key == "staging");

        // The bootstrap default cannot be deleted.
        (await client.DeleteAsync(new Uri("/api/admin/environments/development", UriKind.Relative), ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_non_empty_env_returns_Conflict()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await client.PostAsJsonAsync("/api/admin/environments", new { key = "staging", name = "Staging" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/admin/flags?env=staging", new
        {
            key = "f",
            name = "F",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.DeleteAsync(new Uri("/api/admin/environments/staging", UriKind.Relative), ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
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
