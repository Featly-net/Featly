using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trips for <c>PostgresRoleUpgradeRequestStore</c>, mirroring
/// <c>SqliteRoleUpgradeRequestStoreTests</c>. Status persists as its enum
/// name; the decision fields (decider, comment, decided-at) are nullable
/// until an admin acts.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresRoleUpgradeRequestStoreTests
{
    [Fact]
    public async Task Create_then_get_round_trips_pending_request()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleUpgradeRequestStore;

        var request = new RoleUpgradeRequest
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TargetProjectId = Guid.NewGuid(),
            TargetEnvironmentId = Guid.NewGuid(),
            RequestedRoleId = Guid.NewGuid(),
            Justification = "need editor",
            Status = RoleUpgradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.CreateAsync(request, ct);

        var loaded = await store.GetByIdAsync(request.Id, ct);
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(RoleUpgradeStatus.Pending);
        loaded.Justification.Should().Be("need editor");
        loaded.TargetEnvironmentId.Should().Be(request.TargetEnvironmentId);
        loaded.DecidedAt.Should().BeNull();
        loaded.DecidedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Update_persists_a_decision()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleUpgradeRequestStore;

        var request = new RoleUpgradeRequest
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TargetProjectId = Guid.NewGuid(),
            RequestedRoleId = Guid.NewGuid(),
            Status = RoleUpgradeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.CreateAsync(request, ct);

        var decider = Guid.NewGuid();
        request.Status = RoleUpgradeStatus.Rejected;
        request.DecidedByUserId = decider;
        request.DecisionComment = "not now";
        request.DecidedAt = DateTimeOffset.UtcNow;
        await store.UpdateAsync(request, ct);

        var loaded = await store.GetByIdAsync(request.Id, ct);
        loaded!.Status.Should().Be(RoleUpgradeStatus.Rejected);
        loaded.DecidedByUserId.Should().Be(decider);
        loaded.DecisionComment.Should().Be("not now");
        loaded.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ListByStatus_filters_and_sorts_newest_first()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleUpgradeRequestStore;

        var older = NewPending(DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = NewPending(DateTimeOffset.UtcNow);
        var approved = NewPending(DateTimeOffset.UtcNow.AddMinutes(-5));
        approved.Status = RoleUpgradeStatus.Approved;

        await store.CreateAsync(older, ct);
        await store.CreateAsync(newer, ct);
        await store.CreateAsync(approved, ct);

        var pending = await store.ListByStatusAsync(RoleUpgradeStatus.Pending, ct);
        pending.Select(r => r.Id).Should().Equal(newer.Id, older.Id);

        var all = await store.ListAsync(ct);
        all.Should().HaveCount(3);
    }

    private static RoleUpgradeRequest NewPending(DateTimeOffset createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TargetProjectId = Guid.NewGuid(),
            RequestedRoleId = Guid.NewGuid(),
            Status = RoleUpgradeStatus.Pending,
            CreatedAt = createdAt,
        };
}
