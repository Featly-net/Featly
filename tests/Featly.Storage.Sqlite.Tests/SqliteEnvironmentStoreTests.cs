using FluentAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SqliteEnvironmentStoreTests
{
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
