using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Round-trips for the SQLite-backed role store. Custom roles are mutable
/// through <c>UpsertAsync</c>; system roles are read-only except through the
/// <c>UpsertSystemRoleAsync</c> seed path used by the bootstrap hosted
/// service. Permissions persist as JSON-encoded enum names so a future
/// enum re-ordering doesn't silently change saved role contents.
/// </summary>
public class SqliteRoleStoreTests
{
    [Fact]
    public async Task Seed_system_role_round_trips_with_permissions()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var viewer = SystemRoles.Template(SystemRoles.ViewerKey)!;
        await host.Store.Roles.UpsertSystemRoleAsync(viewer, ct);

        var loaded = await host.Store.Roles.GetByKeyAsync(SystemRoles.ViewerKey, ct);
        loaded.Should().NotBeNull();
        loaded!.IsSystem.Should().BeTrue();
        loaded.Permissions.Should().Contain(Permission.FlagRead);
        loaded.Permissions.Should().Contain(Permission.AuditRead);
        loaded.Permissions.Should().NotContain(Permission.FlagCreate);
    }

    [Fact]
    public async Task UpsertAsync_rejects_writes_to_system_roles()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var admin = SystemRoles.Template(SystemRoles.AdminKey)!;
        await host.Store.Roles.UpsertSystemRoleAsync(admin, ct);

        // Attempting to overwrite the seeded system role via the public path
        // is rejected, regardless of whether the caller marks the new copy
        // as system or not.
        var tampered = SystemRoles.Template(SystemRoles.AdminKey)!;
        tampered.Permissions = [Permission.FlagRead];
        await FluentActions.Awaiting(() => host.Store.Roles.UpsertAsync(tampered, ct))
            .Should().ThrowAsync<InvalidOperationException>();

        var custom = new Role
        {
            Id = Guid.NewGuid(),
            Key = "custom-tries-to-be-system",
            Name = "Tampered",
            IsSystem = true,
            Permissions = [Permission.FlagRead],
        };
        await FluentActions.Awaiting(() => host.Store.Roles.UpsertAsync(custom, ct))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpsertAsync_creates_and_updates_custom_roles()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var custom = new Role
        {
            Id = Guid.NewGuid(),
            Key = "release-captain",
            Name = "Release Captain",
            IsSystem = false,
            Permissions = [Permission.FlagRead, Permission.FlagUpdate, Permission.ChangeApply],
        };
        await host.Store.Roles.UpsertAsync(custom, ct);

        var loaded = await host.Store.Roles.GetByKeyAsync("release-captain", ct);
        loaded!.Permissions.Should().BeEquivalentTo([Permission.FlagRead, Permission.FlagUpdate, Permission.ChangeApply]);

        // Update: replace the permission set.
        custom.Permissions = [Permission.FlagRead];
        await host.Store.Roles.UpsertAsync(custom, ct);

        loaded = await host.Store.Roles.GetByKeyAsync("release-captain", ct);
        loaded!.Permissions.Should().BeEquivalentTo([Permission.FlagRead]);
    }

    [Fact]
    public async Task DeleteAsync_removes_custom_role_but_rejects_system_role()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await host.Store.Roles.UpsertSystemRoleAsync(SystemRoles.Template(SystemRoles.ViewerKey)!, ct);
        await host.Store.Roles.UpsertAsync(new Role
        {
            Id = Guid.NewGuid(),
            Key = "tmp",
            Name = "Temporary",
            Permissions = [Permission.FlagRead],
        }, ct);

        await host.Store.Roles.DeleteAsync("tmp", ct);
        (await host.Store.Roles.GetByKeyAsync("tmp", ct)).Should().BeNull();

        await FluentActions.Awaiting(() => host.Store.Roles.DeleteAsync(SystemRoles.ViewerKey, ct))
            .Should().ThrowAsync<InvalidOperationException>();

        // Missing key is a no-op.
        await host.Store.Roles.DeleteAsync("ghost", ct);
    }

    [Fact]
    public async Task SystemRoles_seed_path_is_idempotent_and_updates_permissions()
    {
        // First seed.
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        await host.Store.Roles.UpsertSystemRoleAsync(SystemRoles.Template(SystemRoles.AdminKey)!, ct);
        var firstId = (await host.Store.Roles.GetByKeyAsync(SystemRoles.AdminKey, ct))!.Id;

        // Re-seed: id stays stable, permissions get refreshed.
        await host.Store.Roles.UpsertSystemRoleAsync(SystemRoles.Template(SystemRoles.AdminKey)!, ct);
        var second = await host.Store.Roles.GetByKeyAsync(SystemRoles.AdminKey, ct);
        second!.Id.Should().Be(firstId);
        second.Permissions.Should().HaveCountGreaterThan(40);
    }
}
