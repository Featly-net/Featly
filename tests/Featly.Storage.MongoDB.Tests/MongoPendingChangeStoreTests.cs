using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.MongoDB.Tests;

[Trait("Category", "RequiresMongoDB")]
public class MongoPendingChangeStoreTests
{
    [Fact]
    public async Task Create_then_get_round_trips_all_fields()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.PendingChangeStore;

        var change = NewChange("Flag", "checkout-flow", Guid.NewGuid());
        await store.CreateAsync(change, ct);

        var loaded = await store.GetByIdAsync(change.Id, ct);
        loaded.Should().NotBeNull();
        loaded!.EntityType.Should().Be("Flag");
        loaded.EntityKey.Should().Be("checkout-flow");
        loaded.Action.Should().Be(ChangeAction.Update);
        loaded.Status.Should().Be(ChangeStatus.Pending);
        loaded.ProposedState.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_persists_approvals_and_comments_and_is_a_noop_for_missing_id()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.PendingChangeStore;

        var change = NewChange("Flag", "checkout-flow", Guid.NewGuid());
        await store.CreateAsync(change, ct);

        var approver = Guid.NewGuid();
        change.Status = ChangeStatus.Approved;
        change.Approvals =
        [
            new ChangeApproval
            {
                Id = Guid.NewGuid(),
                PendingChangeId = change.Id,
                ApproverUserId = approver,
                Decision = ApprovalDecision.Approve,
                At = DateTimeOffset.UtcNow,
            },
        ];
        change.Comments =
        [
            new ChangeComment
            {
                Id = Guid.NewGuid(),
                PendingChangeId = change.Id,
                AuthorUserId = approver,
                Body = "looks good",
                At = DateTimeOffset.UtcNow,
            },
        ];
        change.UpdatedAt = DateTimeOffset.UtcNow;
        await store.UpdateAsync(change, ct);

        var loaded = await store.GetByIdAsync(change.Id, ct);
        loaded!.Status.Should().Be(ChangeStatus.Approved);
        loaded.Approvals.Should().ContainSingle().Which.Decision.Should().Be(ApprovalDecision.Approve);
        loaded.Comments.Should().ContainSingle().Which.Body.Should().Be("looks good");

        // Missing id is a no-op, not a throw.
        var missing = NewChange("Flag", "ghost", Guid.NewGuid());
        await store.UpdateAsync(missing, ct);
    }

    [Fact]
    public async Task TryClaimStatus_wins_exactly_once_for_concurrent_callers()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.PendingChangeStore;

        var change = NewChange("Flag", "checkout-flow", Guid.NewGuid());
        change.Status = ChangeStatus.Approved;
        await store.CreateAsync(change, ct);

        var first = await store.TryClaimStatusAsync(change.Id, ChangeStatus.Approved, ChangeStatus.Applied, ct);
        var second = await store.TryClaimStatusAsync(change.Id, ChangeStatus.Approved, ChangeStatus.Applied, ct);

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await store.GetByIdAsync(change.Id, ct))!.Status.Should().Be(ChangeStatus.Applied);
    }

    [Fact]
    public async Task ListByStatus_and_ListByEnvironment_and_ListOpenForEntity_scope_correctly()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.PendingChangeStore;
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        var pending = NewChange("Flag", "a", envA);
        var approved = NewChange("Flag", "a", envA);
        approved.Status = ChangeStatus.Approved;
        var applied = NewChange("Flag", "a", envA);
        applied.Status = ChangeStatus.Applied;
        var otherEnv = NewChange("Flag", "b", envB);
        otherEnv.Status = ChangeStatus.Applied; // keep the global Pending count scoped to envA below

        await store.CreateAsync(pending, ct);
        await store.CreateAsync(approved, ct);
        await store.CreateAsync(applied, ct);
        await store.CreateAsync(otherEnv, ct);

        (await store.ListByStatusAsync(ChangeStatus.Pending, ct)).Should().ContainSingle().Which.Id.Should().Be(pending.Id);
        (await store.ListByEnvironmentAsync(envA, ct)).Should().HaveCount(3);

        var open = await store.ListOpenForEntityAsync("Flag", "a", envA, ct);
        open.Select(c => c.Id).Should().BeEquivalentTo([pending.Id, approved.Id]);
    }

    private static PendingChange NewChange(string entityType, string entityKey, Guid environmentId) => new()
    {
        Id = Guid.NewGuid(),
        EntityType = entityType,
        EntityKey = entityKey,
        EnvironmentId = environmentId,
        Action = ChangeAction.Update,
        ProposedState = JsonSerializer.SerializeToElement(new { enabled = true }),
        AuthorUserId = Guid.NewGuid(),
        Status = ChangeStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
