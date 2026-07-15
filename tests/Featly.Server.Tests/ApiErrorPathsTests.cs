using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
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
/// Exercises the API's error branches so the RFC 7807 responses (issue #226) are
/// covered: a non-existent <c>?env=</c> and a non-existent entity key both return
/// 404, missing-field requests return 400, and duplicate / rename / state
/// transitions return 409 / 400.
/// </summary>
public class ApiErrorPathsTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";
    private const string BadEnv = "?env=does-not-exist";

    [Fact]
    public async Task Unknown_environment_returns_404_on_reads()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        string[] paths =
        [
            "/api/admin/flags" + BadEnv,
            "/api/admin/flags/x" + BadEnv,
            "/api/admin/configs" + BadEnv,
            "/api/admin/configs/x" + BadEnv,
            "/api/admin/segments" + BadEnv,
            "/api/admin/segments/x" + BadEnv,
            "/api/admin/experiments" + BadEnv,
            "/api/admin/experiments/x" + BadEnv,
            "/api/admin/experiments/x/analytics" + BadEnv,
            "/api/admin/audit" + BadEnv,
        ];

        foreach (var path in paths)
        {
            var response = await c.GetAsync(new Uri(path, UriKind.Relative), ct);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json", path);
        }
    }

    [Fact]
    public async Task Unknown_environment_returns_404_on_writes()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;
        var empty = new { };

        (await c.PostAsJsonAsync("/api/admin/flags" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/flags/x" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/configs" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/configs/x" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/segments" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/segments/x" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/experiments" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/experiments/x" + BadEnv, empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri("/api/admin/experiments/x/start" + BadEnv, UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri("/api/admin/experiments/x/stop" + BadEnv, UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_entity_key_returns_404()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;
        var empty = new { };

        // Default environment resolves; the entity does not.
        (await c.GetAsync(new Uri("/api/admin/flags/nope", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/flags/nope", empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync(new Uri("/api/admin/configs/nope", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/configs/nope", empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync(new Uri("/api/admin/segments/nope", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/segments/nope", empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync(new Uri("/api/admin/experiments/nope", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/experiments/nope", empty, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri("/api/admin/experiments/nope/start", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri("/api/admin/experiments/nope/stop", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync(new Uri($"/api/admin/webhooks/{Guid.NewGuid()}", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/admin/webhooks/{Guid.NewGuid()}", new { name = "n", url = "https://example.com/h" }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync(new Uri($"/api/admin/changes/{Guid.NewGuid()}", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Missing_required_fields_return_400_validation()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        (await c.PostAsJsonAsync("/api/admin/environments", new { key = "", name = "x" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/projects", new { key = "", name = "x" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/groups", new { key = "", name = "x" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/roles", new { key = "", name = "x" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/webhooks", new { name = "", url = "" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/webhooks", new { name = "n", url = "not-a-url" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_rename_and_state_conflicts()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        object flag = new
        {
            key = "dup",
            name = "Dup",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        };
        (await c.PostAsJsonAsync("/api/admin/flags", flag, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Duplicate create -> 409.
        (await c.PostAsJsonAsync("/api/admin/flags", flag, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Rename via PUT (body key != URL key) -> 400.
        object renamed = new
        {
            key = "renamed",
            name = "Dup",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        };
        (await c.PutAsJsonAsync("/api/admin/flags/dup", renamed, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Change_lifecycle_on_unknown_change_returns_404()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        (await c.PostAsJsonAsync($"/api/admin/changes/{id}/comments", new { body = "hi" }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/admin/changes/{id}/approvals", new { decision = "Approve" }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri($"/api/admin/changes/{id}/apply", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri($"/api/admin/changes/{id}/bypass?reason=x", UriKind.Relative), null, ct)).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);

        // Propose with missing required fields -> 400.
        (await c.PostAsJsonAsync("/api/admin/changes", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_create_and_update_with_unknown_environment_returns_404()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var create = new { name = "n", url = "https://example.com/h", eventTypes = Array.Empty<string>(), environmentKey = "does-not-exist" };
        (await c.PostAsJsonAsync("/api/admin/webhooks", create, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Seed a real webhook, then PUT it targeting an unknown environment.
        var seeded = await c.PostAsJsonAsync("/api/admin/webhooks", new { name = "real", url = "https://example.com/ok", eventTypes = Array.Empty<string>() }, ct);
        seeded.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await seeded.Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, ct);
        var update = new { name = "real", url = "https://example.com/ok", eventTypes = Array.Empty<string>(), environmentKey = "does-not-exist" };
        (await c.PutAsJsonAsync($"/api/admin/webhooks/{body!.Id}", update, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rbac_and_directory_endpoints_error_branches()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        // API keys.
        (await c.PostAsJsonAsync("/api/admin/apikeys", new { name = "", scope = "AdminWrite" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/apikeys", new { name = "k", scope = "Nope" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsync(new Uri($"/api/admin/apikeys/{id}/revoke", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync($"/api/admin/apikeys/{id}/rotate", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Groups / roles / users.
        (await c.GetAsync(new Uri("/api/admin/groups/nope", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/groups", new { key = "", name = "g" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.GetAsync(new Uri("/api/admin/roles/nope", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/roles", new { key = "", name = "r" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/roles", new { key = "admin", name = "r", permissions = Array.Empty<string>() }, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.GetAsync(new Uri("/api/admin/users/nope@x.com", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/users", new { identifier = "" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsync(new Uri("/api/admin/users/nope@x.com/disable", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Role upgrade requests + assignments.
        (await c.PostAsync(new Uri($"/api/admin/role-upgrade-requests/{id}/approve", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri($"/api/admin/role-upgrade-requests/{id}/reject", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/role-assignments", new { roleId = id, assigneeType = "User", assigneeId = id }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preview_and_approval_policy_and_settings_error_branches()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        // Preview: unknown env, then unknown key.
        (await c.PostAsJsonAsync("/api/admin/preview/flags/x" + BadEnv, new { }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/preview/flags/nope", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/preview/configs/x" + BadEnv, new { }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync("/api/admin/preview/configs/nope", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // API key minting against an unknown environment.
        (await c.PostAsJsonAsync("/api/admin/apikeys", new { name = "k", scope = "SdkRead", environmentKey = "does-not-exist" }, ct))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);

        // Approval policies are addressed by environment key.
        (await c.GetAsync(new Uri("/api/admin/approval-policies/does-not-exist", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.DeleteAsync(new Uri("/api/admin/approval-policies/does-not-exist", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Settings validation.
        (await c.PutAsJsonAsync("/api/admin/settings/rate-limit", new { enabled = true, authPermitsPerMinute = -1, adminPermitsPerMinute = 1, sdkPermitsPerMinute = 1 }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutAsJsonAsync("/api/admin/settings/audit", new { retentionDays = -5 }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutAsJsonAsync("/api/admin/settings/webhook", new { maxAttempts = 0, baseRetryDelaySeconds = 1, maxRetryDelaySeconds = 1 }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutAsJsonAsync("/api/admin/settings/authorization", new { autoProvisionMode = "Bogus" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Environment_write_error_branches()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        (await c.PostAsJsonAsync("/api/admin/environments", new { key = "development", name = "dupe" }, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PutAsJsonAsync("/api/admin/environments/nope", new { name = "x" }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.DeleteAsync(new Uri("/api/admin/environments/nope", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri("/api/admin/environments/nope/lock", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri("/api/admin/environments/nope/unlock", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Config_and_segment_duplicate_and_rename_conflicts()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        object config = new { key = "cfg", name = "Cfg", type = "Int", defaultValue = 30 };
        (await c.PostAsJsonAsync("/api/admin/configs", config, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.PostAsJsonAsync("/api/admin/configs", config, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PutAsJsonAsync("/api/admin/configs/cfg", new { key = "other", name = "Cfg", type = "Int", defaultValue = 30 }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        object segment = new { key = "seg", name = "Seg", conditions = Array.Empty<object>() };
        (await c.PostAsJsonAsync("/api/admin/segments", segment, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.PostAsJsonAsync("/api/admin/segments", segment, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PutAsJsonAsync("/api/admin/segments/seg", new { key = "other", name = "Seg", conditions = Array.Empty<object>() }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Experiment_validation_and_state_conflicts()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        (await c.PostAsJsonAsync("/api/admin/experiments", new { }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/experiments", new { key = "e", name = "E", flagKey = "ghost" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await c.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "cf",
            name = "CF",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        object exp = new { key = "exp", name = "Exp", flagKey = "cf" };
        (await c.PostAsJsonAsync("/api/admin/experiments", exp, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.PostAsJsonAsync("/api/admin/experiments", exp, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PutAsJsonAsync("/api/admin/experiments/exp", new { key = "other", name = "Exp", flagKey = "cf" }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Stop before start -> 409; start, start-again -> 409; stop, stop-again -> 409.
        (await c.PostAsync(new Uri("/api/admin/experiments/exp/stop", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PostAsync(new Uri("/api/admin/experiments/exp/start", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PostAsync(new Uri("/api/admin/experiments/exp/start", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PostAsync(new Uri("/api/admin/experiments/exp/stop", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PostAsync(new Uri("/api/admin/experiments/exp/stop", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sdk_events_error_branches()
    {
        using var host = await BuildHostAsync();
        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);
        var ct = TestContext.Current.CancellationToken;

        (await sdk.PostAsJsonAsync("/api/sdk/events" + BadEnv, new { events = Array.Empty<object>() }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missingSubject = new { events = new[] { new { type = "Exposure", flagKey = "x", variantKey = "on" } } };
        (await sdk.PostAsJsonAsync("/api/sdk/events", missingSubject, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task More_error_branches_webhooks_roles_settings()
    {
        using var host = await BuildHostAsync();
        var c = Admin(host);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        // Webhook sub-resources on an unknown endpoint.
        (await c.GetAsync(new Uri($"/api/admin/webhooks/{id}/deliveries", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri($"/api/admin/webhooks/{id}/test", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsync(new Uri($"/api/admin/webhooks/{id}/deliveries/{id}/resend", UriKind.Relative), null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Update unknown role / group -> 404.
        (await c.PutAsJsonAsync("/api/admin/roles/nope", new { key = "nope", name = "n", permissions = Array.Empty<string>() }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync("/api/admin/groups/nope", new { key = "nope", name = "n" }, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Role upgrade request for a non-existent role -> 400.
        (await c.PostAsJsonAsync("/api/admin/role-upgrade-requests", new { requestedRoleId = id }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Settings validation (approval defaults + authorization).
        (await c.PutAsJsonAsync("/api/admin/settings/approval-defaults", new { prod = new { minApprovals = 0 }, nonProd = new { minApprovals = 1 } }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Role assignment onto a real role but an unknown assignee -> 400.
        var roles = await c.GetFromJsonAsync<List<Role>>("/api/admin/roles", TestJson.Options, ct);
        var adminRoleId = roles!.First(r => r.Key == SystemRoles.AdminKey).Id;
        (await c.PostAsJsonAsync("/api/admin/role-assignments", new { roleId = adminRoleId, assigneeType = "User", assigneeId = id }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/admin/role-assignments", new { roleId = adminRoleId, assigneeType = "Group", assigneeId = id }, ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpClient Admin(IHost host)
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
