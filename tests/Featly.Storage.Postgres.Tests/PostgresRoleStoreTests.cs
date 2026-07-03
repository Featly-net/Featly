using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trips for <c>PostgresRoleStore</c>, mirroring <c>SqliteRoleStoreTests</c>.
/// Permissions persist as JSON-encoded enum names (<c>PermissionListSerializer</c>)
/// so a future enum re-ordering doesn't silently change saved role contents.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresRoleStoreTests
{
    [Fact]
    public async Task Seed_system_role_round_trips_with_permissions()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleStore;

        var viewer = SystemRoles.Template(SystemRoles.ViewerKey)!;
        await store.UpsertSystemRoleAsync(viewer, ct);

        var loaded = await store.GetByKeyAsync(SystemRoles.ViewerKey, ct);
        loaded.Should().NotBeNull();
        loaded!.IsSystem.Should().BeTrue();
        loaded.Permissions.Should().Contain(Permission.FlagRead);
        loaded.Permissions.Should().Contain(Permission.AuditRead);
        loaded.Permissions.Should().NotContain(Permission.FlagCreate);
    }

    [Fact]
    public async Task UpsertAsync_rejects_writes_to_system_roles()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleStore;

        var admin = SystemRoles.Template(SystemRoles.AdminKey)!;
        await store.UpsertSystemRoleAsync(admin, ct);

        var tampered = SystemRoles.Template(SystemRoles.AdminKey)!;
        tampered.Permissions = [Permission.FlagRead];
        await FluentActions.Awaiting(() => store.UpsertAsync(tampered, ct))
            .Should().ThrowAsync<InvalidOperationException>();

        var custom = new Role
        {
            Id = Guid.NewGuid(),
            Key = "custom-tries-to-be-system",
            Name = "Tampered",
            IsSystem = true,
            Permissions = [Permission.FlagRead],
        };
        await FluentActions.Awaiting(() => store.UpsertAsync(custom, ct))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpsertAsync_creates_and_updates_custom_roles()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleStore;

        var custom = new Role
        {
            Id = Guid.NewGuid(),
            Key = "release-captain",
            Name = "Release Captain",
            IsSystem = false,
            Permissions = [Permission.FlagRead, Permission.FlagUpdate, Permission.ChangeApply],
        };
        await store.UpsertAsync(custom, ct);

        var loaded = await store.GetByKeyAsync("release-captain", ct);
        loaded!.Permissions.Should().BeEquivalentTo([Permission.FlagRead, Permission.FlagUpdate, Permission.ChangeApply]);

        custom.Permissions = [Permission.FlagRead];
        await store.UpsertAsync(custom, ct);

        loaded = await store.GetByKeyAsync("release-captain", ct);
        loaded!.Permissions.Should().BeEquivalentTo([Permission.FlagRead]);
    }

    [Fact]
    public async Task DeleteAsync_removes_custom_role_but_rejects_system_role()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleStore;

        await store.UpsertSystemRoleAsync(SystemRoles.Template(SystemRoles.ViewerKey)!, ct);
        await store.UpsertAsync(new Role
        {
            Id = Guid.NewGuid(),
            Key = "tmp",
            Name = "Temporary",
            Permissions = [Permission.FlagRead],
        }, ct);

        await store.DeleteAsync("tmp", ct);
        (await store.GetByKeyAsync("tmp", ct)).Should().BeNull();

        await FluentActions.Awaiting(() => store.DeleteAsync(SystemRoles.ViewerKey, ct))
            .Should().ThrowAsync<InvalidOperationException>();

        // Missing key is a no-op.
        await store.DeleteAsync("ghost", ct);
    }

    [Fact]
    public async Task SystemRoles_seed_path_is_idempotent_and_updates_permissions()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleStore;

        await store.UpsertSystemRoleAsync(SystemRoles.Template(SystemRoles.AdminKey)!, ct);
        var firstId = (await store.GetByKeyAsync(SystemRoles.AdminKey, ct))!.Id;

        await store.UpsertSystemRoleAsync(SystemRoles.Template(SystemRoles.AdminKey)!, ct);
        var second = await store.GetByKeyAsync(SystemRoles.AdminKey, ct);
        second!.Id.Should().Be(firstId);
        second.Permissions.Should().HaveCountGreaterThan(40);
    }
}
