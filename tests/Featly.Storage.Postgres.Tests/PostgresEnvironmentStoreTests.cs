using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

[Trait("Category", "RequiresPostgres")]
public class PostgresEnvironmentStoreTests
{
    [Fact]
    public async Task CreateAsync_persists_and_round_trips_scoped_to_project()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.EnvironmentStore;
        var projectId = Guid.NewGuid();

        var env = new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Key = "production",
            Name = "Production",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.CreateAsync(env, ct);

        var byKey = await store.GetByKeyAsync(projectId, "production", ct);
        var byId = await store.GetByIdAsync(env.Id, ct);
        var def = await store.GetDefaultAsync(projectId, ct);

        byKey.Should().NotBeNull();
        byId!.Key.Should().Be("production");
        def!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_throws_on_duplicate_key_within_the_same_project()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.EnvironmentStore;
        var projectId = Guid.NewGuid();

        await store.CreateAsync(new Environment { Id = Guid.NewGuid(), ProjectId = projectId, Key = "dev", Name = "Dev", CreatedAt = DateTimeOffset.UtcNow }, ct);

        var duplicate = async () => await store.CreateAsync(
            new Environment { Id = Guid.NewGuid(), ProjectId = projectId, Key = "dev", Name = "Dev 2", CreatedAt = DateTimeOffset.UtcNow }, ct);

        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*dev*");
    }

    [Fact]
    public async Task Same_key_is_allowed_across_different_projects()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.EnvironmentStore;

        await store.CreateAsync(new Environment { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Key = "dev", Name = "Dev A", CreatedAt = DateTimeOffset.UtcNow }, ct);
        await store.CreateAsync(new Environment { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Key = "dev", Name = "Dev B", CreatedAt = DateTimeOffset.UtcNow }, ct);
        // No exception -> the (ProjectId, Key) unique index scopes correctly.
    }

    [Fact]
    public async Task SetReadOnlyAsync_flips_the_freeze_and_returns_null_for_missing_id()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.EnvironmentStore;

        var env = new Environment { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Key = "staging", Name = "Staging", CreatedAt = DateTimeOffset.UtcNow };
        await store.CreateAsync(env, ct);

        var locked = await store.SetReadOnlyAsync(env.Id, readOnly: true, ct);
        locked!.ReadOnly.Should().BeTrue();

        var missing = await store.SetReadOnlyAsync(Guid.NewGuid(), readOnly: true, ct);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_row_and_is_idempotent()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.EnvironmentStore;
        var projectId = Guid.NewGuid();

        var env = new Environment { Id = Guid.NewGuid(), ProjectId = projectId, Key = "temp", Name = "Temp", CreatedAt = DateTimeOffset.UtcNow };
        await store.CreateAsync(env, ct);

        await store.DeleteAsync(env.Id, ct);
        (await store.GetByIdAsync(env.Id, ct)).Should().BeNull();

        // Idempotent: deleting again does not throw.
        await store.DeleteAsync(env.Id, ct);
    }
}
