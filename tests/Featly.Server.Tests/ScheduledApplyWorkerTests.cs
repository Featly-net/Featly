using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Approval;
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
/// ADR-0028 PR 2: <see cref="ScheduledApplyWorker"/> drains due, Approved
/// changes through the exact same <c>ChangeApplicationService</c> +
/// <c>ChangeStaleness</c> path manual Apply uses. These tests drive
/// <see cref="ScheduledApplyWorker.RunOnceAsync"/> directly instead of
/// waiting on <see cref="ScheduledApplyOptions.PollInterval"/>.
/// </summary>
public class ScheduledApplyWorkerTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task Applies_a_due_approved_change_and_publishes_change_applied()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var (_, env) = await DefaultProjectEnvAsync(store, ct);

        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = env.Id,
            Required = true,
            MinApprovals = 1,
            AuthorCanApproveOwnChange = false,
        }, ct);

        using var alice = await CookieClientAsync(host, store, "alice-schedule@example.com", ct, SystemRoles.ApproverKey);
        using var bob = await CookieClientAsync(host, store, "bob-schedule@example.com", ct, SystemRoles.ApproverKey);

        var change = await ProposeCreateAsync(alice, "scheduled-flag", ct);
        await ApproveAsync(bob, change.Id, ct);

        var loaded = (await store.PendingChanges.GetByIdAsync(change.Id, ct))!;
        loaded.Status.Should().Be(ChangeStatus.Approved);
        loaded.ScheduledApplyAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.PendingChanges.UpdateAsync(loaded, ct);

        await Worker(host).RunOnceAsync(ct);

        var reloaded = (await store.PendingChanges.GetByIdAsync(change.Id, ct))!;
        reloaded.Status.Should().Be(ChangeStatus.Applied);
        reloaded.AppliedByUserId.Should().BeNull();

        var flag = await store.Flags.GetAsync(env.Id, "scheduled-flag", ct);
        flag.Should().NotBeNull();
    }

    [Fact]
    public async Task Ignores_an_approved_change_whose_schedule_is_in_the_future()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var (_, env) = await DefaultProjectEnvAsync(store, ct);

        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = env.Id,
            Required = true,
            MinApprovals = 1,
            AuthorCanApproveOwnChange = false,
        }, ct);

        using var alice = await CookieClientAsync(host, store, "alice-future@example.com", ct, SystemRoles.ApproverKey);
        using var bob = await CookieClientAsync(host, store, "bob-future@example.com", ct, SystemRoles.ApproverKey);

        var change = await ProposeCreateAsync(alice, "future-flag", ct);
        await ApproveAsync(bob, change.Id, ct);

        var loaded = (await store.PendingChanges.GetByIdAsync(change.Id, ct))!;
        loaded.ScheduledApplyAt = DateTimeOffset.UtcNow.AddHours(1);
        await store.PendingChanges.UpdateAsync(loaded, ct);

        await Worker(host).RunOnceAsync(ct);

        var reloaded = (await store.PendingChanges.GetByIdAsync(change.Id, ct))!;
        reloaded.Status.Should().Be(ChangeStatus.Approved);
    }

    [Fact]
    public async Task Skips_a_due_change_that_went_stale_since_approval_instead_of_forcing_it()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;
        var (_, env) = await DefaultProjectEnvAsync(store, ct);

        using var admin = AdminClient(host);
        (await admin.PostAsJsonAsync(new Uri("/api/admin/flags", UriKind.Relative), MinimalFlag("stale-flag"), ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await store.ApprovalPolicies.UpsertAsync(new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = env.Id,
            Required = true,
            MinApprovals = 1,
            AuthorCanApproveOwnChange = false,
        }, ct);

        using var alice = await CookieClientAsync(host, store, "alice-stale@example.com", ct, SystemRoles.ApproverKey);
        using var bob = await CookieClientAsync(host, store, "bob-stale@example.com", ct, SystemRoles.ApproverKey);

        var propose = await alice.PostAsJsonAsync(new Uri("/api/admin/changes", UriKind.Relative), new
        {
            entityType = "Flag",
            entityKey = "stale-flag",
            action = "Update",
            proposedState = MinimalFlag("stale-flag"),
        }, ct);
        propose.StatusCode.Should().Be(HttpStatusCode.Created);
        var change = (await propose.Content.ReadFromJsonAsync<PendingChange>(TestJson.Options, ct))!;
        await ApproveAsync(bob, change.Id, ct);

        // Someone else edits the flag directly after approval -- the classic
        // staleness scenario ChangeStaleness exists to catch.
        var live = await store.Flags.GetAsync(env.Id, "stale-flag", ct);
        live!.Name = "Edited out from under the scheduled change";
        live.UpdatedAt = DateTimeOffset.UtcNow;
        await store.Flags.UpsertAsync(env.Id, live, "someone-else", ct);

        var loaded = (await store.PendingChanges.GetByIdAsync(change.Id, ct))!;
        loaded.ScheduledApplyAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.PendingChanges.UpdateAsync(loaded, ct);

        await Worker(host).RunOnceAsync(ct);

        var reloaded = (await store.PendingChanges.GetByIdAsync(change.Id, ct))!;
        reloaded.Status.Should().Be(ChangeStatus.Stale);

        var stillLive = await store.Flags.GetAsync(env.Id, "stale-flag", ct);
        stillLive!.Name.Should().Be("Edited out from under the scheduled change");
    }

    private static ScheduledApplyWorker Worker(IHost host)
        => host.Services.GetServices<IHostedService>().OfType<ScheduledApplyWorker>().Single();

    private static async Task<PendingChange> ProposeCreateAsync(HttpClient client, string key, CancellationToken ct)
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

    private static async Task ApproveAsync(HttpClient client, Guid changeId, CancellationToken ct)
    {
        var resp = await client.PostAsJsonAsync(new Uri($"/api/admin/changes/{changeId}/approvals", UriKind.Relative), new { decision = "Approve" }, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

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

    private static async Task<(Project Project, Environment Env)> DefaultProjectEnvAsync(StorageFacade store, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);
        return (project, env!);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    // Mirrors ChangeWorkflowEndpointsTests.CookieClientAsync: mints a real
    // ApiKey + User + role assignment, then a cookie session, since
    // ChangeActor.ResolveOrCreateAsync rejects the bearer admin pseudo-identity.
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
        var me = await login.Content.ReadFromJsonAsync<MeResponse>(TestJson.Options, ct);
        client.DefaultRequestHeaders.Add("X-Featly-Csrf", me!.CsrfToken);
        return client;
    }

}
