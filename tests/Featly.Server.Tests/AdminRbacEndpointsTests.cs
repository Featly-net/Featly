using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Featly.Server;
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
/// Admin CRUD endpoints for the M7 RBAC entities: users, roles (custom +
/// clone-of-system, system-role immutability), groups, and role assignments.
/// Auth is the admin Bearer key; permission enforcement runs through the
/// per-route <c>RequirePermission</c> filter.
/// </summary>
public class AdminRbacEndpointsTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    // ---------- Roles ----------

    [Fact]
    public async Task Roles_list_includes_the_four_system_roles()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);

        var roles = await client.GetFromJsonAsync<List<Role>>(
            new Uri("/api/admin/roles", UriKind.Relative), TestJson.Options, TestContext.Current.CancellationToken);
        roles!.Select(r => r.Key).Should().Contain([SystemRoles.ViewerKey, SystemRoles.EditorKey, SystemRoles.ApproverKey, SystemRoles.AdminKey]);
    }

    [Fact]
    public async Task Create_custom_role_cloned_from_system_unions_permissions()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);

        var body = new RoleWriteRequest(
            Key: "release-manager",
            Name: "Release Manager",
            Description: "Editor + change approval",
            Permissions: [Permission.ChangeApprove],
            CloneFromSystemRole: SystemRoles.EditorKey);

        var resp = await client.PostAsJsonAsync(new Uri("/api/admin/roles", UriKind.Relative), body, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await resp.Content.ReadFromJsonAsync<Role>(TestJson.Options, cancellationToken: TestContext.Current.CancellationToken);
        created!.IsSystem.Should().BeFalse();
        // Cloned editor permissions...
        created.Permissions.Should().Contain(Permission.FlagCreate);
        // ...plus the explicitly-added one.
        created.Permissions.Should().Contain(Permission.ChangeApprove);
    }

    [Fact]
    public async Task Create_role_with_reserved_system_key_is_conflict()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);

        var body = new RoleWriteRequest(Key: SystemRoles.AdminKey, Name: "Nope", Description: null);
        var resp = await client.PostAsJsonAsync(new Uri("/api/admin/roles", UriKind.Relative), body, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_system_role_is_forbidden()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);

        var body = new RoleWriteRequest(Key: SystemRoles.ViewerKey, Name: "Hacked", Description: null, Permissions: [Permission.FlagCreate]);
        var resp = await client.PutAsJsonAsync(new Uri($"/api/admin/roles/{SystemRoles.ViewerKey}", UriKind.Relative), body, TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Custom_role_update_and_delete_round_trip()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync(new Uri("/api/admin/roles", UriKind.Relative),
            new RoleWriteRequest("temp-role", "Temp", null, [Permission.FlagRead]), ct);

        var update = await client.PutAsJsonAsync(new Uri("/api/admin/roles/temp-role", UriKind.Relative),
            new RoleWriteRequest("temp-role", "Temp 2", "updated", [Permission.FlagRead, Permission.ConfigRead]), ct);
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var del = await client.DeleteAsync(new Uri("/api/admin/roles/temp-role", UriKind.Relative), ct);
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetAsync(new Uri("/api/admin/roles/temp-role", UriKind.Relative), ct);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_system_role_is_forbidden()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);

        var resp = await client.DeleteAsync(new Uri($"/api/admin/roles/{SystemRoles.AdminKey}", UriKind.Relative), TestContext.Current.CancellationToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- Users ----------

    [Fact]
    public async Task Create_user_then_get_and_disable()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var create = await client.PostAsJsonAsync(new Uri("/api/admin/users", UriKind.Relative),
            new UserWriteRequest("kim@example.com", "Kim"), ct);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var get = await client.GetFromJsonAsync<User>(new Uri("/api/admin/users/kim@example.com", UriKind.Relative), TestJson.Options, ct);
        get!.Email.Should().Be("kim@example.com");
        get.Disabled.Should().BeFalse();

        var disable = await client.PostAsync(new Uri("/api/admin/users/kim@example.com/disable", UriKind.Relative), content: null, ct);
        disable.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDisable = await client.GetFromJsonAsync<User>(new Uri("/api/admin/users/kim@example.com", UriKind.Relative), TestJson.Options, ct);
        afterDisable!.Disabled.Should().BeTrue();
    }

    // ---------- Groups ----------

    [Fact]
    public async Task Create_group_with_members_round_trips()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var create = await client.PostAsJsonAsync(new Uri("/api/admin/groups", UriKind.Relative),
            new GroupWriteRequest("security", "Security", "reviewers", [m1, m2]), ct);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var get = await client.GetFromJsonAsync<UserGroup>(new Uri("/api/admin/groups/security", UriKind.Relative), TestJson.Options, ct);
        get!.MemberUserIds.Should().BeEquivalentTo([m1, m2]);
    }

    // ---------- Role assignments ----------

    [Fact]
    public async Task Create_role_assignment_for_user_round_trips()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        // Seed a user and pick a role.
        await store.Users.UpsertAsync(new User
        {
            Id = Guid.NewGuid(),
            Identifier = "leo@example.com",
            DisplayName = "Leo",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }, "test", ct);
        var leo = await store.Users.GetByIdentifierAsync("leo@example.com", ct);
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);

        var body = new RoleAssignmentWriteRequest(AssigneeType.User, leo!.Id, editor!.Id);
        var resp = await client.PostAsJsonAsync(new Uri("/api/admin/role-assignments", UriKind.Relative), body, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<List<RoleAssignment>>(
            new Uri($"/api/admin/role-assignments?assigneeId={leo.Id}", UriKind.Relative), TestJson.Options, ct);
        list!.Should().ContainSingle(a => a.RoleId == editor.Id && a.AssigneeId == leo.Id);
    }

    [Fact]
    public async Task Create_role_assignment_with_unknown_role_is_bad_request()
    {
        using var host = await BuildHostAsync();
        using var client = Admin(host);
        var ct = TestContext.Current.CancellationToken;

        var body = new RoleAssignmentWriteRequest(AssigneeType.User, Guid.NewGuid(), Guid.NewGuid());
        var resp = await client.PostAsJsonAsync(new Uri("/api/admin/role-assignments", UriKind.Relative), body, ct);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Auth gating ----------

    [Fact]
    public async Task Rbac_endpoints_reject_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        foreach (var path in new[] { "/api/admin/users", "/api/admin/roles", "/api/admin/groups", "/api/admin/role-assignments" })
        {
            var resp = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
            resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }
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
