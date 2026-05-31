using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Round-trips for the M8 approval stores: <c>PendingChange</c> (with owned
/// approvals + comments and JsonElement proposed/current state) and
/// <c>ApprovalPolicy</c> (with owned approver rules, one per environment).
/// </summary>
public class SqliteApprovalStoreTests
{
    [Fact]
    public async Task PendingChange_round_trips_state_approvals_and_comments()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var changeId = Guid.NewGuid();
        var change = new PendingChange
        {
            Id = changeId,
            EntityType = "Flag",
            EntityKey = "new-checkout",
            EnvironmentId = Guid.NewGuid(),
            Action = ChangeAction.Update,
            ProposedState = JsonDocument.Parse("""{"enabled":true}""").RootElement,
            CurrentState = JsonDocument.Parse("""{"enabled":false}""").RootElement,
            AuthorUserId = Guid.NewGuid(),
            AuthorMessage = "flip it on",
            Status = ChangeStatus.Pending,
            Approvals =
            [
                new ChangeApproval { Id = Guid.NewGuid(), PendingChangeId = changeId, ApproverUserId = Guid.NewGuid(), Decision = ApprovalDecision.Approve, Comment = "lgtm", At = DateTimeOffset.UtcNow },
            ],
            Comments =
            [
                new ChangeComment { Id = Guid.NewGuid(), PendingChangeId = changeId, AuthorUserId = Guid.NewGuid(), Body = "why now?", At = DateTimeOffset.UtcNow },
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await host.Store.PendingChanges.CreateAsync(change, ct);

        var loaded = await host.Store.PendingChanges.GetByIdAsync(changeId, ct);
        loaded.Should().NotBeNull();
        loaded!.EntityType.Should().Be("Flag");
        loaded.Action.Should().Be(ChangeAction.Update);
        loaded.ProposedState.GetProperty("enabled").GetBoolean().Should().BeTrue();
        loaded.CurrentState!.Value.GetProperty("enabled").GetBoolean().Should().BeFalse();
        loaded.Approvals.Should().ContainSingle(a => a.Decision == ApprovalDecision.Approve && a.Comment == "lgtm");
        loaded.Comments.Should().ContainSingle(c => c.Body == "why now?");
    }

    [Fact]
    public async Task PendingChange_create_with_null_current_state()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var change = new PendingChange
        {
            Id = Guid.NewGuid(),
            EntityType = "Config",
            EntityKey = "timeout",
            EnvironmentId = Guid.NewGuid(),
            Action = ChangeAction.Create,
            ProposedState = JsonDocument.Parse("""{"type":"Int","defaultValue":30}""").RootElement,
            CurrentState = null,
            AuthorUserId = Guid.NewGuid(),
            Status = ChangeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await host.Store.PendingChanges.CreateAsync(change, ct);

        var loaded = await host.Store.PendingChanges.GetByIdAsync(change.Id, ct);
        loaded!.CurrentState.Should().BeNull();
        loaded.Action.Should().Be(ChangeAction.Create);
    }

    [Fact]
    public async Task Update_persists_status_and_decision_fields()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var change = NewPending();
        await host.Store.PendingChanges.CreateAsync(change, ct);

        var applier = Guid.NewGuid();
        change.Status = ChangeStatus.Applied;
        change.AppliedByUserId = applier;
        change.AppliedAt = DateTimeOffset.UtcNow;
        change.UpdatedAt = DateTimeOffset.UtcNow;
        await host.Store.PendingChanges.UpdateAsync(change, ct);

        var loaded = await host.Store.PendingChanges.GetByIdAsync(change.Id, ct);
        loaded!.Status.Should().Be(ChangeStatus.Applied);
        loaded.AppliedByUserId.Should().Be(applier);
        loaded.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ListByStatus_and_ListOpenForEntity_filter_correctly()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var env = Guid.NewGuid();
        var pending = NewPending(env, "Flag", "a");
        var approved = NewPending(env, "Flag", "a");
        approved.Status = ChangeStatus.Approved;
        var appliedOther = NewPending(env, "Flag", "b");
        appliedOther.Status = ChangeStatus.Applied;

        await host.Store.PendingChanges.CreateAsync(pending, ct);
        await host.Store.PendingChanges.CreateAsync(approved, ct);
        await host.Store.PendingChanges.CreateAsync(appliedOther, ct);

        (await host.Store.PendingChanges.ListByStatusAsync(ChangeStatus.Pending, ct)).Should().ContainSingle();
        // Open = Pending or Approved, for entity Flag/a in this env -> 2.
        (await host.Store.PendingChanges.ListOpenForEntityAsync("Flag", "a", env, ct)).Should().HaveCount(2);
        (await host.Store.PendingChanges.ListOpenForEntityAsync("Flag", "b", env, ct)).Should().BeEmpty(); // applied isn't open
    }

    [Fact]
    public async Task ApprovalPolicy_round_trips_rules_and_is_one_per_environment()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var envId = Guid.NewGuid();
        var policy = new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = envId,
            Required = true,
            MinApprovals = 2,
            AuthorCanApproveOwnChange = false,
            AllowEmergencyBypass = true,
            ApproverRules =
            [
                new ApproverRule { Id = Guid.NewGuid(), Type = ApproverRuleType.SpecificUser, UserId = Guid.NewGuid(), Mandatory = true },
                new ApproverRule { Id = Guid.NewGuid(), Type = ApproverRuleType.AnyFromGroup, GroupId = Guid.NewGuid(), Mandatory = true, MinFromThisRule = 1 },
            ],
        };
        await host.Store.ApprovalPolicies.UpsertAsync(policy, ct);

        var loaded = await host.Store.ApprovalPolicies.GetByEnvironmentAsync(envId, ct);
        loaded.Should().NotBeNull();
        loaded!.MinApprovals.Should().Be(2);
        loaded.ApproverRules.Should().HaveCount(2);
        loaded.ApproverRules.Should().Contain(r => r.Type == ApproverRuleType.SpecificUser && r.Mandatory);

        // Upsert again with fewer rules -> replaced, still one row for the env.
        policy.MinApprovals = 1;
        policy.ApproverRules = [];
        await host.Store.ApprovalPolicies.UpsertAsync(policy, ct);
        var reloaded = await host.Store.ApprovalPolicies.GetByEnvironmentAsync(envId, ct);
        reloaded!.MinApprovals.Should().Be(1);
        reloaded.ApproverRules.Should().BeEmpty();

        await host.Store.ApprovalPolicies.DeleteByEnvironmentAsync(envId, ct);
        (await host.Store.ApprovalPolicies.GetByEnvironmentAsync(envId, ct)).Should().BeNull();
    }

    private static PendingChange NewPending(Guid? env = null, string entityType = "Flag", string entityKey = "demo") => new()
    {
        Id = Guid.NewGuid(),
        EntityType = entityType,
        EntityKey = entityKey,
        EnvironmentId = env ?? Guid.NewGuid(),
        Action = ChangeAction.Update,
        ProposedState = JsonDocument.Parse("{}").RootElement,
        AuthorUserId = Guid.NewGuid(),
        Status = ChangeStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
