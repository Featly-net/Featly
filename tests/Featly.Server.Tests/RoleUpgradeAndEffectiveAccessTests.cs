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
/// M7 PR 7D: the role-upgrade-request flow (file / list / approve / reject) and
/// the effective-access view. Approving a request mints the corresponding
/// <see cref="RoleAssignment"/>; the effective-access endpoint explains why a
/// user holds the permissions they do.
/// </summary>
public class RoleUpgradeAndEffectiveAccessTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    // ---------- Effective access ----------

    [Fact]
    public async Task Effective_access_reflects_direct_assignment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "mia@example.com", ct);
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = userId,
            ProjectId = projectId,
            EnvironmentId = null,
            RoleId = editor!.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var access = await client.GetFromJsonAsync<EffectiveAccessResponse>(
            new Uri("/api/admin/users/mia@example.com/effective-access", UriKind.Relative), TestJson.Options, ct);

        access!.Roles.Should().ContainSingle(r => r.Key == SystemRoles.EditorKey && r.Via == AssigneeType.User);
        access.Permissions.Should().Contain(Permission.FlagCreate);
        access.Permissions.Should().NotContain(Permission.RoleCreate);
    }

    [Fact]
    public async Task Effective_access_reflects_group_assignment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "noah@example.com", ct);
        var group = new UserGroup { Id = Guid.NewGuid(), Key = "admins", Name = "Admins", MemberUserIds = [userId], CreatedAt = DateTimeOffset.UtcNow };
        await store.Groups.UpsertAsync(group, ct);
        var admin = await store.Roles.GetByKeyAsync(SystemRoles.AdminKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.Group,
            AssigneeId = group.Id,
            ProjectId = projectId,
            EnvironmentId = null,
            RoleId = admin!.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var access = await client.GetFromJsonAsync<EffectiveAccessResponse>(
            new Uri("/api/admin/users/noah@example.com/effective-access", UriKind.Relative), TestJson.Options, ct);

        access!.Roles.Should().ContainSingle(r => r.Key == SystemRoles.AdminKey && r.Via == AssigneeType.Group);
        access.Permissions.Should().Contain(Permission.RoleCreate);
    }

    [Fact]
    public async Task Effective_access_for_unknown_user_is_404()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);

        var resp = await client.GetAsync(new Uri("/api/admin/users/ghost@example.com/effective-access", UriKind.Relative), TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- Role upgrade requests: admin operations (seeded request) ----------

    [Fact]
    public async Task Approve_mints_assignment_and_marks_approved()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "olivia@example.com", ct);
        var approver = await store.Roles.GetByKeyAsync(SystemRoles.ApproverKey, ct);
        var request = new RoleUpgradeRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetProjectId = projectId,
            RequestedRoleId = approver!.Id,
            Justification = "Need to approve changes",
            Status = RoleUpgradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.RoleUpgradeRequests.CreateAsync(request, ct);

        var resp = await client.PostAsync(new Uri($"/api/admin/role-upgrade-requests/{request.Id}/approve", UriKind.Relative), content: null, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Request marked approved.
        var updated = await store.RoleUpgradeRequests.GetByIdAsync(request.Id, ct);
        updated!.Status.Should().Be(RoleUpgradeStatus.Approved);
        updated.DecidedAt.Should().NotBeNull();

        // Assignment minted for the user with the requested role.
        var assignments = await store.RoleAssignments.ListForAssigneeAsync(userId, ct);
        assignments.Should().ContainSingle(a => a.RoleId == approver.Id && a.ProjectId == projectId);
    }

    [Fact]
    public async Task Reject_marks_rejected_with_comment_and_mints_no_assignment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "peter@example.com", ct);
        var admin = await store.Roles.GetByKeyAsync(SystemRoles.AdminKey, ct);
        var request = new RoleUpgradeRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetProjectId = projectId,
            RequestedRoleId = admin!.Id,
            Status = RoleUpgradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.RoleUpgradeRequests.CreateAsync(request, ct);

        var resp = await client.PostAsJsonAsync(
            new Uri($"/api/admin/role-upgrade-requests/{request.Id}/reject", UriKind.Relative),
            new DecisionRequest("Too broad — ask for editor instead"), ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await store.RoleUpgradeRequests.GetByIdAsync(request.Id, ct);
        updated!.Status.Should().Be(RoleUpgradeStatus.Rejected);
        updated.DecisionComment.Should().Contain("editor");

        (await store.RoleAssignments.ListForAssigneeAsync(userId, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_already_decided_is_conflict()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "quinn@example.com", ct);
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        var request = new RoleUpgradeRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetProjectId = projectId,
            RequestedRoleId = editor!.Id,
            Status = RoleUpgradeStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.RoleUpgradeRequests.CreateAsync(request, ct);

        var resp = await client.PostAsync(new Uri($"/api/admin/role-upgrade-requests/{request.Id}/approve", UriKind.Relative), content: null, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "rosa@example.com", ct);
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        await store.RoleUpgradeRequests.CreateAsync(Req(userId, projectId, editor!.Id, RoleUpgradeStatus.Pending), ct);
        await store.RoleUpgradeRequests.CreateAsync(Req(userId, projectId, editor.Id, RoleUpgradeStatus.Approved), ct);

        var pending = await client.GetFromJsonAsync<List<RoleUpgradeRequest>>(
            new Uri("/api/admin/role-upgrade-requests?status=Pending", UriKind.Relative), TestJson.Options, ct);
        pending!.Should().OnlyContain(r => r.Status == RoleUpgradeStatus.Pending);

        var all = await client.GetFromJsonAsync<List<RoleUpgradeRequest>>(
            new Uri("/api/admin/role-upgrade-requests", UriKind.Relative), TestJson.Options, ct);
        all!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    // ---------- Filing via a real cookie identity ----------

    [Fact]
    public async Task File_request_via_cookie_identity_round_trips()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var hasher = host.Services.GetRequiredService<ApiKeyHasher>();
        using var client = host.GetTestClient();
        var ct = TestContext.Current.CancellationToken;

        // Mint a real ApiKey whose Name is a human identifier, so the cookie
        // session carries that identity (not the legacy api-key:* pseudonym).
        var plaintext = hasher.GenerateToken();
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);
        await store.ApiKeys.CreateAsync(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "sam@example.com",
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = env!.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        }, ct);

        var login = await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new LoginRequest(plaintext), ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        using var file = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/admin/role-upgrade-requests", UriKind.Relative))
        {
            Content = JsonContent.Create(new RoleUpgradeRequestWriteRequest(editor!.Id, Justification: "Please")),
        };
        file.Headers.Add("Cookie", cookie);

        var resp = await client.SendAsync(file, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        // The filer user row was resolved/created and the request points at it.
        var sam = await store.Users.GetByIdentifierAsync("sam@example.com", ct);
        sam.Should().NotBeNull();
        var requests = await store.RoleUpgradeRequests.ListAsync(ct);
        requests.Should().ContainSingle(r => r.UserId == sam!.Id && r.Status == RoleUpgradeStatus.Pending);
    }

    [Fact]
    public async Task Filing_rejects_legacy_api_key_identity()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        var resp = await client.PostAsJsonAsync(
            new Uri("/api/admin/role-upgrade-requests", UriKind.Relative),
            new RoleUpgradeRequestWriteRequest(editor!.Id, Justification: "from a bot"), ct);

        // api-key:* identities are not real users; filing is refused.
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static RoleUpgradeRequest Req(Guid userId, Guid projectId, Guid roleId, RoleUpgradeStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetProjectId = projectId,
            RequestedRoleId = roleId,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task<(Guid UserId, Guid ProjectId)> SeedUserAndProjectAsync(StorageFacade store, string identifier, CancellationToken ct)
    {
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
        var project = await store.Projects.GetDefaultAsync(ct);
        return (user!.Id, project!.Id);
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
