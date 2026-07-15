using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SqliteEnvironmentStoreTests
{
    [Fact]
    public async Task BumpConfigVersion_increments_and_is_idempotent_for_a_missing_id()
    {
        // The SDK ETag is this counter (issue #228): every snapshot write bumps it.
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var id = Guid.NewGuid();
        await host.Store.Environments.CreateAsync(NewEnvironment(id, Guid.NewGuid(), "production"), ct);
        (await host.Store.Environments.GetByIdAsync(id, ct))!.ConfigVersion.Should().Be(0);

        await host.Store.Environments.BumpConfigVersionAsync(id, ct);
        await host.Store.Environments.BumpConfigVersionAsync(id, ct);
        (await host.Store.Environments.GetByIdAsync(id, ct))!.ConfigVersion.Should().Be(2);

        // A bump for an environment that no longer exists is a no-op, not a throw.
        var missing = async () => await host.Store.Environments.BumpConfigVersionAsync(Guid.NewGuid(), ct);
        await missing.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Default_list_update_and_delete_round_trip()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var project = Guid.NewGuid();

        var defaultId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var isDefault = NewEnvironment(defaultId, project, "development");
        isDefault.IsDefault = true;
        await host.Store.Environments.CreateAsync(isDefault, ct);
        await host.Store.Environments.CreateAsync(NewEnvironment(other, project, "staging"), ct);

        (await host.Store.Environments.GetDefaultAsync(project, ct))!.Id.Should().Be(defaultId);
        (await host.Store.Environments.GetDefaultAsync(Guid.NewGuid(), ct)).Should().BeNull();

        var all = await host.Store.Environments.ListAsync(project, ct);
        all.Select(e => e.Key).Should().Equal("development", "staging"); // ordered by key

        // Update persists the name; the key is immutable.
        var renamed = NewEnvironment(other, project, "staging");
        renamed.Name = "Staging (EU)";
        await host.Store.Environments.UpdateAsync(renamed, ct);
        (await host.Store.Environments.GetByIdAsync(other, ct))!.Name.Should().Be("Staging (EU)");

        var missing = async () => await host.Store.Environments.UpdateAsync(NewEnvironment(Guid.NewGuid(), project, "ghost"), ct);
        await missing.Should().ThrowAsync<InvalidOperationException>();

        await host.Store.Environments.DeleteAsync(other, ct);
        (await host.Store.Environments.GetByIdAsync(other, ct)).Should().BeNull();
        // Deleting a missing row is a no-op.
        await host.Store.Environments.DeleteAsync(Guid.NewGuid(), ct);
        (await host.Store.Environments.ListAsync(project, ct)).Should().ContainSingle();
    }

    private static Environment NewEnvironment(Guid id, Guid projectId, string key) => new()
    {
        Id = id,
        ProjectId = projectId,
        Key = key,
        Name = key,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Same_environment_key_can_coexist_across_projects()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        await host.Store.Environments.CreateAsync(new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectA,
            Key = "production",
            Name = "Production",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        await host.Store.Environments.CreateAsync(new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectB,
            Key = "production",
            Name = "Production",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        var inA = await host.Store.Environments.GetByKeyAsync(projectA, "production", ct);
        var inB = await host.Store.Environments.GetByKeyAsync(projectB, "production", ct);

        inA.Should().NotBeNull();
        inB.Should().NotBeNull();
        inA!.ProjectId.Should().Be(projectA);
        inB!.ProjectId.Should().Be(projectB);
    }

    [Fact]
    public async Task Duplicate_key_within_same_project_throws()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();

        await host.Store.Environments.CreateAsync(new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Key = "staging",
            Name = "Staging",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        var duplicate = async () => await host.Store.Environments.CreateAsync(new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Key = "staging",
            Name = "Staging again",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        await duplicate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*staging*");
    }

    [Fact]
    public async Task SetReadOnly_persists_the_freeze_flag()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var id = Guid.NewGuid();

        await host.Store.Environments.CreateAsync(new Environment
        {
            Id = id,
            ProjectId = projectId,
            Key = "production",
            Name = "Production",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        var locked = await host.Store.Environments.SetReadOnlyAsync(id, true, ct);
        locked!.ReadOnly.Should().BeTrue();
        (await host.Store.Environments.GetByIdAsync(id, ct))!.ReadOnly.Should().BeTrue();

        var unlocked = await host.Store.Environments.SetReadOnlyAsync(id, false, ct);
        unlocked!.ReadOnly.Should().BeFalse();
        (await host.Store.Environments.GetByIdAsync(id, ct))!.ReadOnly.Should().BeFalse();

        // Missing id returns null.
        (await host.Store.Environments.SetReadOnlyAsync(Guid.NewGuid(), true, ct)).Should().BeNull();
    }
}
