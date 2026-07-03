using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trips for <c>PostgresUserGroupStore</c>, mirroring
/// <c>SqliteUserGroupStoreTests</c>. Membership is stored as a native
/// <c>uuid[]</c> column (vs. SQLite's JSON array); <c>ListForMemberAsync</c>
/// is the lookup the permission checker uses to expand a user into its
/// groups, so the array-containment translation gets the most coverage.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresUserGroupStoreTests
{
    [Fact]
    public async Task Upsert_then_get_round_trips_membership()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.UserGroupStore;

        var members = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var group = new UserGroup
        {
            Id = Guid.NewGuid(),
            Key = "security",
            Name = "Security",
            Description = "Security reviewers",
            MemberUserIds = members,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.UpsertAsync(group, ct);

        var loaded = await store.GetByKeyAsync("security", ct);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Security");
        loaded.MemberUserIds.Should().BeEquivalentTo(members);
    }

    [Fact]
    public async Task Upsert_overwrites_membership_and_keeps_id()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.UserGroupStore;

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var group = new UserGroup
        {
            Id = Guid.NewGuid(),
            Key = "team",
            Name = "Team",
            MemberUserIds = [alice],
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.UpsertAsync(group, ct);
        var firstId = (await store.GetByKeyAsync("team", ct))!.Id;

        await store.UpsertAsync(new UserGroup
        {
            Id = Guid.NewGuid(), // ignored on update — key is the natural key
            Key = "team",
            Name = "Team Renamed",
            MemberUserIds = [alice, bob],
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        var loaded = await store.GetByKeyAsync("team", ct);
        loaded!.Id.Should().Be(firstId);
        loaded.Name.Should().Be("Team Renamed");
        loaded.MemberUserIds.Should().BeEquivalentTo([alice, bob]);
    }

    [Fact]
    public async Task ListForMember_returns_only_groups_containing_the_user()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.UserGroupStore;

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await store.UpsertAsync(NewGroup("g1", [alice, bob]), ct);
        await store.UpsertAsync(NewGroup("g2", [alice]), ct);
        await store.UpsertAsync(NewGroup("g3", [bob]), ct);

        var aliceGroups = await store.ListForMemberAsync(alice, ct);
        aliceGroups.Select(g => g.Key).Should().BeEquivalentTo(["g1", "g2"]);

        var bobGroups = await store.ListForMemberAsync(bob, ct);
        bobGroups.Select(g => g.Key).Should().BeEquivalentTo(["g1", "g3"]);

        var strangerGroups = await store.ListForMemberAsync(Guid.NewGuid(), ct);
        strangerGroups.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.UserGroupStore;

        await store.UpsertAsync(NewGroup("temp", [Guid.NewGuid()]), ct);
        await store.DeleteAsync("temp", ct);
        (await store.GetByKeyAsync("temp", ct)).Should().BeNull();

        // Second delete on a missing key is a no-op.
        await store.DeleteAsync("temp", ct);
        await store.DeleteAsync("never-existed", ct);
    }

    private static UserGroup NewGroup(string key, List<Guid> members)
        => new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = key,
            MemberUserIds = members,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
