using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Authentication;
using Featly.Server.Endpoints;
using Featly.Storage.InMemory;
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
/// M8 PR 8B: the change-request lifecycle over HTTP — propose, comment, approve
/// (policy re-evaluated each time), apply (mutates the entity), reject, and
/// emergency bypass. Distinct human identities are obtained via cookie sessions
/// backed by real <see cref="ApiKey"/> rows, since the legacy admin bearer is a
/// single non-user pseudonym.
/// </summary>
public class ChangeWorkflowEndpointsTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task Propose_approve_apply_round_trips_and_mutates_the_flag()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var (_, env) = await DefaultProjectEnvAsync(store, ct);

        // Policy: 1 approval, author can't approve own change.
        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = env.Id,
            Required = true,
            MinApprovals = 1,
            AuthorCanApproveOwnChange = false,
        }, ct);

        // Alice is an approver too, so she CAN submit an approval — but as the
        // author it won't count toward satisfaction.
        using var alice = await CookieClientAsync(host, store, "alice@example.com", ct, SystemRoles.ApproverKey);
        using var bob = await CookieClientAsync(host, store, "bob@example.com", ct, SystemRoles.ApproverKey);

        // Alice proposes creating a flag.
        var proposed = new
        {
            key = "new-flag",
            name = "New Flag",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = (object)true },
                new { key = "off", name = "Off", value = (object)false },
            },
        };
        var propose = await alice.PostAsJsonAsync(new Uri("/api/admin/changes", UriKind.Relative), new
        {
            entityType = "Flag",
            entityKey = "new-flag",
            action = "Create",
            proposedState = proposed,
        }, ct);
        propose.StatusCode.Should().Be(HttpStatusCode.Created);
        var change = await propose.Content.ReadFromJsonAsync<PendingChange>(TestJson.Options, ct);
        change!.Status.Should().Be(ChangeStatus.Pending);

        // Author's own approval doesn't satisfy (AuthorCanApproveOwnChange=false).
        var selfApprove = await alice.PostAsJsonAsync(new Uri($"/api/admin/changes/{change.Id}/approvals", UriKind.Relative),
            new { decision = "Approve" }, ct);
        selfApprove.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.PendingChanges.GetByIdAsync(change.Id, ct))!.Status.Should().Be(ChangeStatus.Pending);

        // Bob approves -> satisfied -> Approved.
        var bobApprove = await bob.PostAsJsonAsync(new Uri($"/api/admin/changes/{change.Id}/approvals", UriKind.Relative),
            new { decision = "Approve", comment = "lgtm" }, ct);
        bobApprove.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.PendingChanges.GetByIdAsync(change.Id, ct))!.Status.Should().Be(ChangeStatus.Approved);

        // The flag doesn't exist yet (not applied).
        (await store.Flags.GetAsync(env.Id, "new-flag", ct)).Should().BeNull();

        // Apply -> flag created.
        var apply = await bob.PostAsync(new Uri($"/api/admin/changes/{change.Id}/apply", UriKind.Relative), content: null, ct);
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.PendingChanges.GetByIdAsync(change.Id, ct))!.Status.Should().Be(ChangeStatus.Applied);
        var flag = await store.Flags.GetAsync(env.Id, "new-flag", ct);
        flag.Should().NotBeNull();
        flag!.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Applying_a_gated_change_preserves_prerequisites()
    {
        // Regression coverage: ChangeApplicationService.ApplyFlagAsync must
        // copy Prerequisites onto the existing entity like every other
        // field -- an earlier version silently dropped them on apply even
        // though the direct (non-gated) PUT path persisted them correctly.
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var (_, env) = await DefaultProjectEnvAsync(store, ct);

        using var admin = AdminClient(host);
        // The infra flag exists before the environment goes ReadOnly-by-policy.
        (await admin.PostAsJsonAsync(new Uri("/api/admin/flags", UriKind.Relative), MinimalFlag("infra-flag"), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync(new Uri("/api/admin/flags", UriKind.Relative), MinimalFlag("gated-flag"), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = env.Id,
            Required = true,
            MinApprovals = 1,
            AuthorCanApproveOwnChange = false,
        }, ct);

        using var alice = await CookieClientAsync(host, store, "alice2@example.com", ct, SystemRoles.ApproverKey);
        using var bob = await CookieClientAsync(host, store, "bob2@example.com", ct, SystemRoles.ApproverKey);

        var updateBody = new
        {
            key = "gated-flag",
            name = "gated-flag",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = (object)true },
                new { key = "off", name = "Off", value = (object)false },
            },
            prerequisites = new[] { new { flagKey = "infra-flag", requiredVariantKeys = new[] { "on" } } },
        };
        var propose = await alice.PostAsJsonAsync(new Uri("/api/admin/changes", UriKind.Relative), new
        {
            entityType = "Flag",
            entityKey = "gated-flag",
            action = "Update",
            proposedState = updateBody,
        }, ct);
        propose.StatusCode.Should().Be(HttpStatusCode.Created);
        var change = (await propose.Content.ReadFromJsonAsync<PendingChange>(TestJson.Options, ct))!;

        await bob.PostAsJsonAsync(new Uri($"/api/admin/changes/{change.Id}/approvals", UriKind.Relative), new { decision = "Approve" }, ct);
        (await bob.PostAsync(new Uri($"/api/admin/changes/{change.Id}/apply", UriKind.Relative), content: null, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var applied = await store.Flags.GetAsync(env.Id, "gated-flag", ct);
        applied!.Prerequisites.Should().ContainSingle(p => p.FlagKey == "infra-flag");
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    [Fact]
    public async Task Reject_marks_change_rejected_and_blocks_apply()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var (_, env) = await DefaultProjectEnvAsync(store, ct);
        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy { Id = Guid.NewGuid(), EnvironmentId = env.Id, Required = true, MinApprovals = 1 }, ct);

        using var alice = await CookieClientAsync(host, store, "alice@example.com", ct);
        using var bob = await CookieClientAsync(host, store, "bob@example.com", ct, SystemRoles.ApproverKey);

        var change = await ProposeFlagAsync(alice, "rej-flag", ct);

        var reject = await bob.PostAsJsonAsync(new Uri($"/api/admin/changes/{change.Id}/approvals", UriKind.Relative),
            new { decision = "Reject", comment = "no" }, ct);
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.PendingChanges.GetByIdAsync(change.Id, ct))!.Status.Should().Be(ChangeStatus.Rejected);

        var apply = await bob.PostAsync(new Uri($"/api/admin/changes/{change.Id}/apply", UriKind.Relative), content: null, ct);
        apply.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Emergency_bypass_applies_immediately_and_flags_the_change()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var (_, env) = await DefaultProjectEnvAsync(store, ct);
        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy { Id = Guid.NewGuid(), EnvironmentId = env.Id, Required = true, MinApprovals = 2, AllowEmergencyBypass = true }, ct);

        using var alice = await CookieClientAsync(host, store, "alice@example.com", ct);
        var change = await ProposeFlagAsync(alice, "incident-flag", ct);

        // Bypass with the admin bearer (operational).
        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var bypass = await admin.PostAsJsonAsync(new Uri($"/api/admin/changes/{change.Id}/bypass", UriKind.Relative),
            new { reason = "incident-1234" }, ct);
        bypass.StatusCode.Should().Be(HttpStatusCode.OK);

        var applied = await store.PendingChanges.GetByIdAsync(change.Id, ct);
        applied!.Status.Should().Be(ChangeStatus.Applied);
        applied.WasEmergencyBypass.Should().BeTrue();
        applied.EmergencyReason.Should().Be("incident-1234");
        (await store.Flags.GetAsync(env.Id, "incident-flag", ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Bypass_requires_a_reason()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        await DefaultProjectEnvAsync(store, ct);

        using var alice = await CookieClientAsync(host, store, "alice@example.com", ct);
        var change = await ProposeFlagAsync(alice, "x-flag", ct);

        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var bypass = await admin.PostAsJsonAsync(new Uri($"/api/admin/changes/{change.Id}/bypass", UriKind.Relative),
            new { reason = "" }, ct);
        bypass.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Comment_is_appended_to_the_change()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        await DefaultProjectEnvAsync(store, ct);

        using var alice = await CookieClientAsync(host, store, "alice@example.com", ct);
        var change = await ProposeFlagAsync(alice, "discuss-flag", ct);

        var comment = await alice.PostAsJsonAsync(new Uri($"/api/admin/changes/{change.Id}/comments", UriKind.Relative),
            new { body = "why now?" }, ct);
        comment.StatusCode.Should().Be(HttpStatusCode.OK);
        (await store.PendingChanges.GetByIdAsync(change.Id, ct))!.Comments.Should().ContainSingle(c => c.Body == "why now?");
    }

    [Fact]
    public async Task Propose_with_legacy_api_key_is_rejected()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        await DefaultProjectEnvAsync(store, ct);

        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var propose = await admin.PostAsJsonAsync(new Uri("/api/admin/changes", UriKind.Relative), new
        {
            entityType = "Flag",
            entityKey = "bot-flag",
            action = "Create",
            proposedState = MinimalFlag("bot-flag"),
        }, ct);
        propose.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approval_policy_crud_round_trips()
    {
        using var host = await BuildHostAsync();
        using var admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        var put = await admin.PutAsJsonAsync(new Uri("/api/admin/approval-policies/development", UriKind.Relative),
            new { required = true, minApprovals = 2, authorCanApproveOwnChange = false, allowEmergencyBypass = true }, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await admin.GetFromJsonAsync<ApprovalPolicy>(new Uri("/api/admin/approval-policies/development", UriKind.Relative), TestJson.Options, ct);
        get!.Required.Should().BeTrue();
        get.MinApprovals.Should().Be(2);

        var del = await admin.DeleteAsync(new Uri("/api/admin/approval-policies/development", UriKind.Relative), ct);
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await admin.GetFromJsonAsync<ApprovalPolicy>(new Uri("/api/admin/approval-policies/development", UriKind.Relative), TestJson.Options, ct);
        afterDelete!.Required.Should().BeFalse(); // no policy => not required
    }

    // ---------- helpers ----------

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

    private static async Task<PendingChange> ProposeFlagAsync(HttpClient client, string key, CancellationToken ct)
    {
        var resp = await client.PostAsJsonAsync(new Uri("/api/admin/changes", UriKind.Relative), new
        {
            entityType = "Flag",
            entityKey = key,
            action = "Create",
            proposedState = MinimalFlag(key),
        }, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<PendingChange>(TestJson.Options, ct))!;
    }

    private static async Task<(Project Project, Environment Env)> DefaultProjectEnvAsync(StorageFacade store, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);
        return (project, env!);
    }

    // Creates a real user with the given system role assigned (so permission
    // enforcement passes), mints an ApiKey for them, and returns a client whose
    // cookie session carries that human identity.
    private static async Task<HttpClient> CookieClientAsync(IHost host, StorageFacade store, string identifier, CancellationToken ct, string roleKey = SystemRoles.EditorKey)
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

        // Provision the user row and grant the role on the default project.
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
        // Cookie-authenticated mutations must echo the session's anti-forgery
        // token (FeatlyCsrfFilter), exactly like the dashboard does.
        var me = await login.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, ct);
        client.DefaultRequestHeaders.Add("X-Featly-Csrf", me!.CsrfToken);
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
