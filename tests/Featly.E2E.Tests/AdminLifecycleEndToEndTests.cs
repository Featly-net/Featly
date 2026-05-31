using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

namespace Featly.E2E.Tests;

/// <summary>
/// End-to-end coverage for the newer admin surfaces that the dashboard now
/// drives: user/group membership, role assignments + effective access, API key
/// mint/revoke, and the audit log's before/after payload. Exercises the real
/// HTTP endpoints behind the admin auth policy.
/// </summary>
public class AdminLifecycleEndToEndTests
{
    private const string AdminKey = "admin-key-e2e";
    private const string SdkKey = "sdk-key-e2e";

    [Fact]
    public async Task User_joins_group_and_gets_a_role_that_shows_in_effective_access()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        // Create a user and a group.
        var user = await (await admin.PostAsJsonAsync("/api/admin/users",
            new { identifier = "alice@example.com", displayName = "Alice" }, ct))
            .Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var userId = user.GetProperty("id").GetGuid();

        var createGroup = await admin.PostAsJsonAsync("/api/admin/groups", new { key = "qa", name = "QA" }, ct);
        createGroup.StatusCode.Should().Be(HttpStatusCode.Created);

        // Add the user to the group via the same payload the picker sends.
        var put = await admin.PutAsJsonAsync("/api/admin/groups/qa",
            new { key = "qa", name = "QA", memberUserIds = new[] { userId } }, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var group = await admin.GetFromJsonAsync<JsonElement>("/api/admin/groups/qa", ct);
        group.GetProperty("memberUserIds").EnumerateArray().Select(e => e.GetGuid()).Should().Contain(userId);

        // Assign the system "viewer" role directly to the user.
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/admin/roles", ct);
        var viewerId = roles.EnumerateArray().Single(r => r.GetProperty("key").GetString() == "viewer").GetProperty("id").GetGuid();

        var assign = await admin.PostAsJsonAsync("/api/admin/role-assignments",
            new { assigneeType = "User", assigneeId = userId, roleId = viewerId }, ct);
        assign.StatusCode.Should().Be(HttpStatusCode.Created);

        // Effective access should now include the viewer role.
        var access = await admin.GetFromJsonAsync<JsonElement>("/api/admin/users/alice@example.com/effective-access", ct);
        access.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("key").GetString())
            .Should().Contain("viewer");

        // The assignment is auditable.
        var audit = await admin.GetFromJsonAsync<JsonElement>("/api/admin/audit?entityType=RoleAssignment", ct);
        audit.EnumerateArray().Select(e => e.GetProperty("action").GetString())
            .Should().Contain(FeatlyEventTypes.RoleAssigned);
    }

    [Fact]
    public async Task ApiKey_mint_appears_in_the_list_and_can_be_revoked()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var mint = await admin.PostAsJsonAsync("/api/admin/apikeys", new { name = "ci-pipeline", scope = "SdkRead" }, ct);
        mint.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/apikeys", ct);
        var key = list.EnumerateArray().Single(k => k.GetProperty("name").GetString() == "ci-pipeline");
        key.GetProperty("revoked").GetBoolean().Should().BeFalse();
        var id = key.GetProperty("id").GetGuid();

        var revoke = await admin.PostAsync(new Uri($"/api/admin/apikeys/{id}/revoke", UriKind.Relative), null, ct);
        revoke.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Accepted);

        var after = await admin.GetFromJsonAsync<JsonElement>("/api/admin/apikeys", ct);
        after.EnumerateArray().Single(k => k.GetProperty("id").GetGuid() == id)
            .GetProperty("revoked").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Audit_log_records_a_flag_update_with_before_and_after()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "demo",
            name = "Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct);
        await admin.PutAsJsonAsync("/api/admin/flags/demo", new
        {
            key = "demo",
            name = "Demo (edited)",
            type = "Boolean",
            enabled = false,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct);

        var audit = await admin.GetFromJsonAsync<JsonElement>("/api/admin/audit?entityType=Flag", ct);
        var updated = audit.EnumerateArray().Single(e => e.GetProperty("action").GetString() == FeatlyEventTypes.FlagUpdated);
        var data = updated.GetProperty("data");
        data.GetProperty("before").GetProperty("name").GetString().Should().Be("Demo");
        data.GetProperty("before").GetProperty("enabled").GetBoolean().Should().BeTrue();
        data.GetProperty("after").GetProperty("name").GetString().Should().Be("Demo (edited)");
        data.GetProperty("after").GetProperty("enabled").GetBoolean().Should().BeFalse();
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
