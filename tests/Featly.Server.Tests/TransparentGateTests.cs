using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Featly.Server;
using Featly.Server.Authentication;
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
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Tests;

/// <summary>
/// M8 PR 8C: the transparent approval gate. When an environment's policy
/// requires approval, ordinary flag/config/segment mutations turn into a
/// 202 PendingChange instead of applying; <c>?dryRun</c> reports without
/// mutating; <c>?emergency=true&amp;reason=</c> applies immediately with an
/// audit trail; and an approved change goes stale if the entity moves under it.
/// </summary>
public class TransparentGateTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task Mutation_is_gated_to_202_when_policy_requires_approval()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var env = await RequireApprovalAsync(store, ct);

        using var editor = await CookieClientAsync(host, store, "alice@example.com", SystemRoles.EditorKey, ct);
        var resp = await editor.PostAsJsonAsync(new Uri("/api/admin/flags?env=development", UriKind.Relative), MinimalFlag("gated-flag"), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        // Not applied directly.
        (await store.Flags.GetAsync(env.Id, "gated-flag", ct)).Should().BeNull();
        // A pending change exists.
        (await store.PendingChanges.ListByStatusAsync(ChangeStatus.Pending, ct)).Should().ContainSingle(c => c.EntityKey == "gated-flag");
    }

    [Fact]
    public async Task DryRun_reports_without_mutating()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var env = await RequireApprovalAsync(store, ct);

        using var editor = await CookieClientAsync(host, store, "alice@example.com", SystemRoles.EditorKey, ct);
        var resp = await editor.PostAsJsonAsync(new Uri("/api/admin/flags?env=development&dryRun=true", UriKind.Relative), MinimalFlag("dry-flag"), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElementWrapper>(TestJson.Options, ct);
        doc!.WouldRequireApproval.Should().BeTrue();
        (await store.Flags.GetAsync(env.Id, "dry-flag", ct)).Should().BeNull();
        (await store.PendingChanges.ListAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Emergency_bypass_applies_immediately_with_audit()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var env = await RequireApprovalAsync(store, ct);

        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var resp = await admin.PostAsJsonAsync(new Uri("/api/admin/flags?env=development&emergency=true&reason=incident-9", UriKind.Relative), MinimalFlag("hot-flag"), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.Flags.GetAsync(env.Id, "hot-flag", ct)).Should().NotBeNull();
        var change = (await store.PendingChanges.ListAsync(ct)).Single(c => c.EntityKey == "hot-flag");
        change.Status.Should().Be(ChangeStatus.Applied);
        change.WasEmergencyBypass.Should().BeTrue();
        change.EmergencyReason.Should().Be("incident-9");
    }

    [Fact]
    public async Task Emergency_without_reason_is_bad_request()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        await RequireApprovalAsync(store, ct);

        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var resp = await admin.PostAsJsonAsync(new Uri("/api/admin/flags?env=development&emergency=true", UriKind.Relative), MinimalFlag("x"), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task No_policy_applies_directly()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);

        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var resp = await admin.PostAsJsonAsync(new Uri("/api/admin/flags?env=development", UriKind.Relative), MinimalFlag("direct-flag"), ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        (await store.Flags.GetAsync(env!.Id, "direct-flag", ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Approved_change_goes_stale_when_entity_moves_underneath()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);

        // Create the flag directly first (no policy yet).
        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        (await admin.PostAsJsonAsync(new Uri("/api/admin/flags?env=development", UriKind.Relative), MinimalFlag("move-flag"), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Now require approval.
        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy { Id = Guid.NewGuid(), EnvironmentId = env!.Id, Required = true, MinApprovals = 1, AllowEmergencyBypass = true }, ct);

        using var alice = await CookieClientAsync(host, store, "alice@example.com", SystemRoles.EditorKey, ct);
        using var bob = await CookieClientAsync(host, store, "bob@example.com", SystemRoles.ApproverKey, ct);

        // Alice proposes an update (gated -> 202).
        var propose = await alice.PutAsJsonAsync(new Uri("/api/admin/flags/move-flag?env=development", UriKind.Relative), EnabledFlag("move-flag", true), ct);
        propose.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var change = await propose.Content.ReadFromJsonAsync<PendingChange>(TestJson.Options, ct);

        // Bob approves -> Approved.
        await bob.PostAsJsonAsync(new Uri($"/api/admin/changes/{change!.Id}/approvals", UriKind.Relative), new { decision = "Approve" }, ct);
        (await store.PendingChanges.GetByIdAsync(change.Id, ct))!.Status.Should().Be(ChangeStatus.Approved);

        // Meanwhile the flag moves underneath via emergency bypass (PUT = update).
        (await admin.PutAsJsonAsync(new Uri("/api/admin/flags/move-flag?env=development&emergency=true&reason=hotfix", UriKind.Relative), EnabledFlag("move-flag", false), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Applying the now-rebased-needed change is rejected as stale.
        var apply = await bob.PostAsync(new Uri($"/api/admin/changes/{change.Id}/apply", UriKind.Relative), content: null, ct);
        apply.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await store.PendingChanges.GetByIdAsync(change.Id, ct))!.Status.Should().Be(ChangeStatus.Stale);
    }

    // ---------- helpers ----------

    private sealed record JsonElementWrapper(bool DryRun, bool WouldRequireApproval, string EntityType, string EntityKey, string Action);

    private static object MinimalFlag(string key) => new
    {
        key,
        name = key,
        type = "Boolean",
        enabled = false,
        defaultVariantKey = "off",
        variants = new[]
        {
            new { key = "on", name = "On", value = (object)true },
            new { key = "off", name = "Off", value = (object)false },
        },
    };

    private static object EnabledFlag(string key, bool enabled) => new
    {
        key,
        name = key,
        type = "Boolean",
        enabled,
        defaultVariantKey = enabled ? "on" : "off",
        variants = new[]
        {
            new { key = "on", name = "On", value = (object)true },
            new { key = "off", name = "Off", value = (object)false },
        },
    };

    private static async Task<Environment> RequireApprovalAsync(StorageFacade store, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);
        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = env!.Id,
            Required = true,
            MinApprovals = 1,
            AllowEmergencyBypass = true,
        }, ct);
        return env;
    }

    private static async Task<HttpClient> CookieClientAsync(IHost host, StorageFacade store, string identifier, string roleKey, CancellationToken ct)
    {
        var hasher = host.Services.GetRequiredService<ApiKeyHasher>();
        var plaintext = hasher.GenerateToken();
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);
        await store.ApiKeys.CreateAsync(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = identifier,
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = env!.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        }, ct);

        var now = DateTimeOffset.UtcNow;
        await store.Users.UpsertAsync(new User
        {
            Id = Guid.NewGuid(),
            Identifier = identifier,
            DisplayName = identifier,
            Email = identifier,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "test",
            UpdatedBy = "test",
        }, "test", ct);
        var user = await store.Users.GetByIdentifierAsync(identifier, ct);
        var role = await store.Roles.GetByKeyAsync(roleKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = user!.Id,
            ProjectId = project.Id,
            EnvironmentId = null,
            RoleId = role!.Id,
            AssignedAt = now,
        }, ct);

        var client = host.GetTestClient();
        var login = await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new LoginRequest(plaintext), ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
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
