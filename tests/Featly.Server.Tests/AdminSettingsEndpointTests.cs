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
/// Covers the DB-overridable settings endpoints: GET returns the effective value
/// + precedence source, PUT persists the database singleton (flipping the source
/// to Database) and validates input, behind the SettingsRead/SettingsUpdate gates.
/// </summary>
public class AdminSettingsEndpointTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task GET_webhook_rejects_unauthenticated()
    {
        using var host = await BuildHostAsync();
        var response = await host.GetTestClient().GetAsync("/api/admin/settings/webhook", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_webhook_rejects_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);
        var response = await client.GetAsync("/api/admin/settings/webhook", TestContext.Current.CancellationToken);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GET_webhook_returns_effective_defaults_when_no_db_override()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);

        var view = await client.GetFromJsonAsync<JsonElement>("/api/admin/settings/webhook", TestContext.Current.CancellationToken);
        view.GetProperty("value").GetProperty("maxAttempts").GetInt32().Should().Be(6);
        view.GetProperty("source").GetString().Should().BeOneOf("HardcodedDefault", "AppSettings");
    }

    [Fact]
    public async Task PUT_webhook_persists_and_flips_source_to_database()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var put = await client.PutAsJsonAsync("/api/admin/settings/webhook", new
        {
            maxAttempts = 12,
            baseRetryDelaySeconds = 4,
            maxRetryDelaySeconds = 900,
        }, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterPut = await put.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        afterPut.GetProperty("source").GetString().Should().Be("Database");
        afterPut.GetProperty("value").GetProperty("maxAttempts").GetInt32().Should().Be(12);

        // A fresh GET reflects the persisted override.
        var view = await client.GetFromJsonAsync<JsonElement>("/api/admin/settings/webhook", ct);
        view.GetProperty("source").GetString().Should().Be("Database");
        view.GetProperty("value").GetProperty("baseRetryDelaySeconds").GetInt32().Should().Be(4);
        view.GetProperty("value").GetProperty("maxRetryDelaySeconds").GetInt32().Should().Be(900);
    }

    [Fact]
    public async Task PUT_webhook_rejects_invalid_values()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        (await client.PutAsJsonAsync("/api/admin/settings/webhook", new { maxAttempts = 0, baseRetryDelaySeconds = 1, maxRetryDelaySeconds = 10 }, ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PutAsJsonAsync("/api/admin/settings/webhook", new { maxAttempts = 5, baseRetryDelaySeconds = 100, maxRetryDelaySeconds = 10 }, ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_webhook_records_an_audit_entry()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        await client.PutAsJsonAsync("/api/admin/settings/webhook", new { maxAttempts = 7, baseRetryDelaySeconds = 2, maxRetryDelaySeconds = 60 }, ct);

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/admin/audit?entityType=Settings", ct);
        audit.GetArrayLength().Should().BeGreaterThan(0);
        audit[0].GetProperty("action").GetString().Should().Be("setting.changed");
    }

    [Fact]
    public async Task GET_authorization_returns_open_by_default()
    {
        using var host = await BuildHostAsync();
        var view = await AdminClient(host).GetFromJsonAsync<JsonElement>("/api/admin/settings/authorization", TestContext.Current.CancellationToken);
        view.GetProperty("value").GetProperty("autoProvisionMode").GetString().Should().Be("Open");
        view.GetProperty("source").GetString().Should().BeOneOf("HardcodedDefault", "AppSettings");
    }

    [Fact]
    public async Task PUT_authorization_persists_and_flips_source_to_database()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var put = await client.PutAsJsonAsync("/api/admin/settings/authorization", new { autoProvisionMode = "Closed" }, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await put.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        after.GetProperty("source").GetString().Should().Be("Database");
        after.GetProperty("value").GetProperty("autoProvisionMode").GetString().Should().Be("Closed");

        var view = await client.GetFromJsonAsync<JsonElement>("/api/admin/settings/authorization", ct);
        view.GetProperty("source").GetString().Should().Be("Database");
        view.GetProperty("value").GetProperty("autoProvisionMode").GetString().Should().Be("Closed");
    }

    [Fact]
    public async Task PUT_audit_persists_retention_and_rejects_negative()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var def = await client.GetFromJsonAsync<JsonElement>("/api/admin/settings/audit", ct);
        def.GetProperty("value").GetProperty("retentionDays").GetInt32().Should().Be(0);

        var put = await client.PutAsJsonAsync("/api/admin/settings/audit", new { retentionDays = 30 }, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await put.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        after.GetProperty("source").GetString().Should().Be("Database");
        after.GetProperty("value").GetProperty("retentionDays").GetInt32().Should().Be(30);

        (await client.PutAsJsonAsync("/api/admin/settings/audit", new { retentionDays = -1 }, ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_approval_defaults_persists_templates()
    {
        using var host = await BuildHostAsync();
        var client = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var def = await client.GetFromJsonAsync<JsonElement>("/api/admin/settings/approval-defaults", ct);
        def.GetProperty("value").GetProperty("prod").GetProperty("required").GetBoolean().Should().BeFalse();

        var put = await client.PutAsJsonAsync("/api/admin/settings/approval-defaults", new
        {
            prod = new { required = true, minApprovals = 2, authorCanApproveOwnChange = false, allowEmergencyBypass = true },
            nonProd = new { required = false, minApprovals = 1, authorCanApproveOwnChange = false, allowEmergencyBypass = true },
        }, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var view = await client.GetFromJsonAsync<JsonElement>("/api/admin/settings/approval-defaults", ct);
        view.GetProperty("source").GetString().Should().Be("Database");
        view.GetProperty("value").GetProperty("prod").GetProperty("required").GetBoolean().Should().BeTrue();
        view.GetProperty("value").GetProperty("prod").GetProperty("minApprovals").GetInt32().Should().Be(2);

        (await client.PutAsJsonAsync("/api/admin/settings/approval-defaults", new { prod = new { minApprovals = 0 }, nonProd = new { } }, ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

        return await builder.StartAsync(TestContext.Current.CancellationToken);
    }
}
