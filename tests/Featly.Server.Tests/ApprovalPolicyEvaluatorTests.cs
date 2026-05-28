using System.Text.Json;
using Featly.Server.Approval;
using FluentAssertions;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// The combinatorial core of M8: deciding whether a <see cref="PendingChange"/>
/// satisfies its <see cref="ApprovalPolicy"/>. Pure, storage-free — membership
/// resolution is injected as the <c>matchesRule</c> delegate.
/// </summary>
public class ApprovalPolicyEvaluatorTests
{
    private static readonly Guid Author = Guid.NewGuid();

    [Fact]
    public void MinApprovals_not_met_is_not_satisfied()
    {
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 2 };
        var change = ChangeWith(Approve(Guid.NewGuid()));

        var result = ApprovalPolicyEvaluator.Evaluate(policy, change, (_, _) => true);

        result.Satisfied.Should().BeFalse();
        result.ApprovalsCount.Should().Be(1);
        result.Outstanding.Should().ContainMatch("1 more approval*");
    }

    [Fact]
    public void MinApprovals_met_with_no_rules_is_satisfied()
    {
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 2 };
        var change = ChangeWith(Approve(Guid.NewGuid()), Approve(Guid.NewGuid()));

        var result = ApprovalPolicyEvaluator.Evaluate(policy, change, (_, _) => true);

        result.Satisfied.Should().BeTrue();
        result.Outstanding.Should().BeEmpty();
    }

    [Fact]
    public void Any_reject_blocks_even_if_count_met()
    {
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 1 };
        var change = ChangeWith(Approve(Guid.NewGuid()), new ChangeApproval
        {
            Id = Guid.NewGuid(),
            PendingChangeId = Guid.NewGuid(),
            ApproverUserId = Guid.NewGuid(),
            Decision = ApprovalDecision.Reject,
        });

        var result = ApprovalPolicyEvaluator.Evaluate(policy, change, (_, _) => true);

        result.Rejected.Should().BeTrue();
        result.Satisfied.Should().BeFalse();
    }

    [Fact]
    public void Author_self_approval_does_not_count_by_default()
    {
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 1 };
        var change = ChangeWith(Approve(Author)); // only the author approved

        var result = ApprovalPolicyEvaluator.Evaluate(policy, change, (_, _) => true);

        result.ApprovalsCount.Should().Be(0);
        result.Satisfied.Should().BeFalse();
    }

    [Fact]
    public void Author_self_approval_counts_when_allowed()
    {
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 1, AuthorCanApproveOwnChange = true };
        var change = ChangeWith(Approve(Author));

        var result = ApprovalPolicyEvaluator.Evaluate(policy, change, (_, _) => true);

        result.Satisfied.Should().BeTrue();
    }

    [Fact]
    public void Duplicate_approvals_from_same_user_count_once()
    {
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 2 };
        var bob = Guid.NewGuid();
        var change = ChangeWith(Approve(bob), Approve(bob));

        var result = ApprovalPolicyEvaluator.Evaluate(policy, change, (_, _) => true);

        result.ApprovalsCount.Should().Be(1);
        result.Satisfied.Should().BeFalse();
    }

    [Fact]
    public void Mandatory_specific_user_must_approve()
    {
        var carlos = Guid.NewGuid();
        var rule = new ApproverRule { Id = Guid.NewGuid(), Type = ApproverRuleType.SpecificUser, UserId = carlos, Mandatory = true };
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 1, ApproverRules = [rule] };

        // Someone else approved, not Carlos -> count met but mandatory rule unmet.
        var other = Guid.NewGuid();
        var change = ChangeWith(Approve(other));
        var matches = (Guid uid, ApproverRule r) => r.UserId == uid;

        var beforeCarlos = ApprovalPolicyEvaluator.Evaluate(policy, change, matches);
        beforeCarlos.Satisfied.Should().BeFalse();
        beforeCarlos.Outstanding.Should().ContainMatch("*specific required user*");

        // Now Carlos approves too.
        var change2 = ChangeWith(Approve(other), Approve(carlos));
        var afterCarlos = ApprovalPolicyEvaluator.Evaluate(policy, change2, matches);
        afterCarlos.Satisfied.Should().BeTrue();
    }

    [Fact]
    public void Mandatory_group_rule_needs_min_from_group()
    {
        var securityGroup = Guid.NewGuid();
        var rule = new ApproverRule { Id = Guid.NewGuid(), Type = ApproverRuleType.AnyFromGroup, GroupId = securityGroup, Mandatory = true, MinFromThisRule = 2 };
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 2, ApproverRules = [rule] };

        var sec1 = Guid.NewGuid();
        var sec2 = Guid.NewGuid();
        // matchesRule: sec1/sec2 are in the security group.
        var matches = (Guid uid, ApproverRule r) => r.GroupId == securityGroup && (uid == sec1 || uid == sec2);

        var oneFromSecurity = ChangeWith(Approve(sec1), Approve(Guid.NewGuid()));
        ApprovalPolicyEvaluator.Evaluate(policy, oneFromSecurity, matches).Satisfied.Should().BeFalse();

        var twoFromSecurity = ChangeWith(Approve(sec1), Approve(sec2));
        ApprovalPolicyEvaluator.Evaluate(policy, twoFromSecurity, matches).Satisfied.Should().BeTrue();
    }

    [Fact]
    public void Realistic_combination_carlos_plus_one_from_security()
    {
        var carlos = Guid.NewGuid();
        var securityGroup = Guid.NewGuid();
        var policy = new ApprovalPolicy
        {
            EnvironmentId = Guid.NewGuid(),
            Required = true,
            MinApprovals = 2,
            ApproverRules =
            [
                new ApproverRule { Id = Guid.NewGuid(), Type = ApproverRuleType.SpecificUser, UserId = carlos, Mandatory = true },
                new ApproverRule { Id = Guid.NewGuid(), Type = ApproverRuleType.AnyFromGroup, GroupId = securityGroup, Mandatory = true, MinFromThisRule = 1 },
            ],
        };
        var secMember = Guid.NewGuid();
        var matches = (Guid uid, ApproverRule r) =>
            (r.Type == ApproverRuleType.SpecificUser && r.UserId == uid) ||
            (r.Type == ApproverRuleType.AnyFromGroup && r.GroupId == securityGroup && uid == secMember);

        // Only Carlos so far: total 1 < 2, security rule unmet.
        ApprovalPolicyEvaluator.Evaluate(policy, ChangeWith(Approve(carlos)), matches).Satisfied.Should().BeFalse();

        // Carlos + a security member: total 2, both mandatory rules met.
        ApprovalPolicyEvaluator.Evaluate(policy, ChangeWith(Approve(carlos), Approve(secMember)), matches).Satisfied.Should().BeTrue();
    }

    [Fact]
    public void RequestChanges_neither_approves_nor_rejects()
    {
        var policy = new ApprovalPolicy { EnvironmentId = Guid.NewGuid(), Required = true, MinApprovals = 1 };
        var change = ChangeWith(new ChangeApproval
        {
            Id = Guid.NewGuid(),
            PendingChangeId = Guid.NewGuid(),
            ApproverUserId = Guid.NewGuid(),
            Decision = ApprovalDecision.RequestChanges,
        });

        var result = ApprovalPolicyEvaluator.Evaluate(policy, change, (_, _) => true);
        result.Rejected.Should().BeFalse();
        result.Satisfied.Should().BeFalse();
        result.ApprovalsCount.Should().Be(0);
    }

    private static ChangeApproval Approve(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        PendingChangeId = Guid.NewGuid(),
        ApproverUserId = userId,
        Decision = ApprovalDecision.Approve,
    };

    private static PendingChange ChangeWith(params ChangeApproval[] approvals) => new()
    {
        Id = Guid.NewGuid(),
        EntityType = "Flag",
        EntityKey = "demo",
        EnvironmentId = Guid.NewGuid(),
        Action = ChangeAction.Update,
        ProposedState = JsonDocument.Parse("{}").RootElement,
        AuthorUserId = Author,
        Status = ChangeStatus.Pending,
        Approvals = [.. approvals],
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
