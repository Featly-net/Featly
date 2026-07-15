using AwesomeAssertions;
using Featly.Authorization;
using Featly.Server;
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
/// Verifies M7 PR 7A's <c>DefaultFeatlyPermissionChecker</c>: real users resolve
/// through <see cref="RoleAssignment"/> rows (union of matching roles), the
/// legacy api-keys and bootstrap admin keep their hardcoded shortcut, and the
/// no-assignment fallback honors <c>AutoProvisionMode</c>.
/// </summary>
public class DefaultPermissionCheckerTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task Legacy_admin_api_key_resolves_to_admin()
    {
        using var host = await BuildHostAsync();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var admin = new ResolvedUser("api-key:AdminWrite", "Admin");
        (await checker.HasAsync(admin, Guid.Empty, null, Permission.FlagCreate, ct)).Should().BeTrue();
        (await checker.HasAsync(admin, Guid.Empty, null, Permission.RoleCreate, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_sdk_api_key_resolves_to_viewer()
    {
        using var host = await BuildHostAsync();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var sdk = new ResolvedUser("api-key:SdkRead", "Sdk");
        (await checker.HasAsync(sdk, Guid.Empty, null, Permission.FlagRead, ct)).Should().BeTrue();
        (await checker.HasAsync(sdk, Guid.Empty, null, Permission.FlagCreate, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Bootstrap_admin_identifier_resolves_to_admin()
    {
        using var host = await BuildHostAsync(bootstrapAdmin: "alice@example.com");
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var alice = new ResolvedUser("alice@example.com", "Alice");
        (await checker.HasAsync(alice, Guid.Empty, null, Permission.RoleCreate, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task User_with_editor_assignment_on_default_project_gets_editor_permissions()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "carol@example.com", ct);
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = userId,
            ProjectId = projectId,
            EnvironmentId = null, // project-wide
            RoleId = editor!.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var carol = new ResolvedUser("carol@example.com", "Carol");
        // Editor can create/update flags...
        (await checker.HasAsync(carol, Guid.Empty, null, Permission.FlagCreate, ct)).Should().BeTrue();
        (await checker.HasAsync(carol, Guid.Empty, null, Permission.FlagUpdate, ct)).Should().BeTrue();
        // ...but not admin-only governance.
        (await checker.HasAsync(carol, Guid.Empty, null, Permission.RoleCreate, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Assignment_union_takes_the_more_permissive_role()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "dave@example.com", ct);
        var viewer = await store.Roles.GetByKeyAsync(SystemRoles.ViewerKey, ct);
        var admin = await store.Roles.GetByKeyAsync(SystemRoles.AdminKey, ct);
        await store.RoleAssignments.CreateAsync(Assignment(userId, projectId, viewer!.Id), ct);
        await store.RoleAssignments.CreateAsync(Assignment(userId, projectId, admin!.Id), ct);

        var dave = new ResolvedUser("dave@example.com", "Dave");
        // Union of viewer + admin => admin wins (more is more, no deny rules).
        (await checker.HasAsync(dave, Guid.Empty, null, Permission.RoleCreate, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Env_scoped_assignment_only_matches_that_environment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "erin@example.com", ct);
        var envId = Guid.NewGuid();
        var otherEnvId = Guid.NewGuid();
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = userId,
            ProjectId = projectId,
            EnvironmentId = envId, // scoped to one environment
            RoleId = editor!.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var erin = new ResolvedUser("erin@example.com", "Erin");
        // Matches when the request targets that env.
        (await checker.HasAsync(erin, projectId, envId, Permission.FlagCreate, ct)).Should().BeTrue();
        // Does not match a different env (and Closed mode would deny — here Open
        // gives viewer floor, so FlagCreate is still false either way).
        (await checker.HasAsync(erin, projectId, otherEnvId, Permission.FlagCreate, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task User_inherits_role_from_group_assignment()
    {
        using var host = await BuildHostAsync(autoProvisionMode: "Closed"); // isolate the group grant
        var store = host.Services.GetRequiredService<StorageFacade>();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "ivan@example.com", ct);

        // Ivan belongs to the "security" group; the group (not Ivan directly)
        // is assigned the editor role.
        var group = new UserGroup
        {
            Id = Guid.NewGuid(),
            Key = "security",
            Name = "Security",
            MemberUserIds = [userId],
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.Groups.UpsertAsync(group, ct);

        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.Group,
            AssigneeId = group.Id,
            ProjectId = projectId,
            EnvironmentId = null,
            RoleId = editor!.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var ivan = new ResolvedUser("ivan@example.com", "Ivan");
        // Closed mode + no direct assignment, but the group grants editor.
        (await checker.HasAsync(ivan, Guid.Empty, null, Permission.FlagCreate, ct)).Should().BeTrue();
        (await checker.HasAsync(ivan, Guid.Empty, null, Permission.RoleCreate, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Non_member_does_not_inherit_group_role()
    {
        using var host = await BuildHostAsync(autoProvisionMode: "Closed");
        var store = host.Services.GetRequiredService<StorageFacade>();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var (memberId, projectId) = await SeedUserAndProjectAsync(store, "judy@example.com", ct);
        await SeedUserAndProjectAsync(store, "outsider@example.com", ct);

        var group = new UserGroup
        {
            Id = Guid.NewGuid(),
            Key = "admins",
            Name = "Admins",
            MemberUserIds = [memberId],
            CreatedAt = DateTimeOffset.UtcNow,
        };
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

        var member = new ResolvedUser("judy@example.com", "Judy");
        var outsider = new ResolvedUser("outsider@example.com", "Outsider");
        (await checker.HasAsync(member, Guid.Empty, null, Permission.RoleCreate, ct)).Should().BeTrue();
        (await checker.HasAsync(outsider, Guid.Empty, null, Permission.RoleCreate, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Open_mode_gives_unassigned_user_the_viewer_floor()
    {
        using var host = await BuildHostAsync(); // AutoProvisionMode defaults to Open
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var stranger = new ResolvedUser("frank@example.com", "Frank");
        (await checker.HasAsync(stranger, Guid.Empty, null, Permission.FlagRead, ct)).Should().BeTrue();
        (await checker.HasAsync(stranger, Guid.Empty, null, Permission.FlagCreate, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Closed_mode_denies_unassigned_user()
    {
        using var host = await BuildHostAsync(autoProvisionMode: "Closed");
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var stranger = new ResolvedUser("grace@example.com", "Grace");
        (await checker.HasAsync(stranger, Guid.Empty, null, Permission.FlagRead, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Closed_mode_still_honors_explicit_assignment()
    {
        using var host = await BuildHostAsync(autoProvisionMode: "Closed");
        var store = host.Services.GetRequiredService<StorageFacade>();
        var checker = host.Services.GetRequiredService<IFeatlyPermissionChecker>();
        var ct = TestContext.Current.CancellationToken;

        var (userId, projectId) = await SeedUserAndProjectAsync(store, "heidi@example.com", ct);
        var viewer = await store.Roles.GetByKeyAsync(SystemRoles.ViewerKey, ct);
        await store.RoleAssignments.CreateAsync(Assignment(userId, projectId, viewer!.Id), ct);

        var heidi = new ResolvedUser("heidi@example.com", "Heidi");
        (await checker.HasAsync(heidi, Guid.Empty, null, Permission.FlagRead, ct)).Should().BeTrue();
        (await checker.HasAsync(heidi, Guid.Empty, null, Permission.FlagCreate, ct)).Should().BeFalse();
    }

    private static RoleAssignment Assignment(Guid userId, Guid projectId, Guid roleId)
        => new()
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = userId,
            ProjectId = projectId,
            EnvironmentId = null,
            RoleId = roleId,
            AssignedAt = DateTimeOffset.UtcNow,
        };

    private static async Task<(Guid UserId, Guid ProjectId)> SeedUserAndProjectAsync(
        StorageFacade store, string identifier, CancellationToken ct)
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

    private static async Task<IHost> BuildHostAsync(string bootstrapAdmin = "", string? autoProvisionMode = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["Featly:Authorization:BootstrapAdminIdentifier"] = bootstrapAdmin,
        };
        if (autoProvisionMode is not null)
        {
            config["Featly:Authorization:AutoProvisionMode"] = autoProvisionMode;
        }

        return await FeatlyTestHost.CreateAsync(config);
    }
}
