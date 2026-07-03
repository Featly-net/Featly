using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trip coverage for <c>PostgresProjectStore</c>, mirroring
/// <c>SqliteProjectStoreTests</c>. Runs against a real, throwaway Postgres
/// database per test (see <see cref="PostgresTestHost"/>) — requires
/// <c>FEATLY_POSTGRES_TEST_*</c> env vars or a local Postgres on 15432.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresProjectStoreTests
{
    [Fact]
    public async Task CreateAsync_persists_and_round_trips_via_key_and_id()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ProjectStore;

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default project",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await store.CreateAsync(project, ct);

        var byKey = await store.GetByKeyAsync("default", ct);
        var byId = await store.GetByIdAsync(project.Id, ct);
        var def = await store.GetDefaultAsync(ct);

        byKey.Should().NotBeNull();
        byKey!.Id.Should().Be(project.Id);
        byId.Should().NotBeNull();
        byId!.Key.Should().Be("default");
        def.Should().NotBeNull();
        def!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_throws_on_duplicate_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ProjectStore;

        await store.CreateAsync(new Project
        {
            Id = Guid.NewGuid(),
            Key = "alpha",
            Name = "Alpha",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        var duplicate = async () => await store.CreateAsync(new Project
        {
            Id = Guid.NewGuid(),
            Key = "alpha",
            Name = "Alpha clone",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        await duplicate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*alpha*");
    }

    [Fact]
    public async Task UpdateAsync_changes_name_and_description_but_not_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ProjectStore;

        var project = new Project { Id = Guid.NewGuid(), Key = "beta", Name = "Beta", CreatedAt = DateTimeOffset.UtcNow };
        await store.CreateAsync(project, ct);

        project.Name = "Beta Renamed";
        project.Description = "now with a description";
        await store.UpdateAsync(project, ct);

        var updated = await store.GetByKeyAsync("beta", ct);
        updated!.Name.Should().Be("Beta Renamed");
        updated.Description.Should().Be("now with a description");
    }

    [Fact]
    public async Task ListAsync_returns_every_project_ordered_by_key()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ProjectStore;

        await store.CreateAsync(new Project { Id = Guid.NewGuid(), Key = "zeta", Name = "Zeta", CreatedAt = DateTimeOffset.UtcNow }, ct);
        await store.CreateAsync(new Project { Id = Guid.NewGuid(), Key = "alpha", Name = "Alpha", CreatedAt = DateTimeOffset.UtcNow }, ct);

        var list = await store.ListAsync(ct);

        list.Select(p => p.Key).Should().Equal("alpha", "zeta");
    }
}
