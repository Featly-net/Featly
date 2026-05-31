using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

public class SqliteProjectStoreTests
{
    [Fact]
    public async Task CreateAsync_persists_and_round_trips_via_key_and_id()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default project",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await host.Store.Projects.CreateAsync(project, ct);

        var byKey = await host.Store.Projects.GetByKeyAsync("default", ct);
        var byId = await host.Store.Projects.GetByIdAsync(project.Id, ct);
        var def = await host.Store.Projects.GetDefaultAsync(ct);

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
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        await host.Store.Projects.CreateAsync(new Project
        {
            Id = Guid.NewGuid(),
            Key = "alpha",
            Name = "Alpha",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        var duplicate = async () => await host.Store.Projects.CreateAsync(new Project
        {
            Id = Guid.NewGuid(),
            Key = "alpha",
            Name = "Alpha clone",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        await duplicate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*alpha*");
    }
}
