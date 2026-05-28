namespace Featly.Server.Approval;

/// <summary>
/// Pure evaluation of whether a <see cref="PendingChange"/> satisfies its
/// environment's <see cref="ApprovalPolicy"/> (ARCHITECTURE.md §12). Kept free
/// of storage so it can be unit-tested directly: the caller resolves role /
/// group membership and passes it in as the <c>matchesRule</c> delegate.
/// </summary>
/// <remarks>
/// A change is approvable when:
/// <list type="number">
///   <item>No approver submitted <see cref="ApprovalDecision.Reject"/>.</item>
///   <item>The count of distinct approving users (excluding the author unless
///         <see cref="ApprovalPolicy.AuthorCanApproveOwnChange"/>) is at least
///         <see cref="ApprovalPolicy.MinApprovals"/>.</item>
///   <item>Every <em>mandatory</em> <see cref="ApproverRule"/> is satisfied —
///         the specific user approved, or at least
///         <see cref="ApproverRule.MinFromThisRule"/> distinct approvers match
///         the rule's role / group.</item>
/// </list>
/// <see cref="ApprovalDecision.RequestChanges"/> neither approves nor rejects.
/// </remarks>
public static class ApprovalPolicyEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="change"/> against <paramref name="policy"/>.
    /// <paramref name="matchesRule"/> answers "does this approver satisfy this
    /// rule" (specific-user equality, role membership, or group membership).
    /// </summary>
    public static ApprovalEvaluation Evaluate(
        ApprovalPolicy policy,
        PendingChange change,
        Func<Guid, ApproverRule, bool> matchesRule)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(matchesRule);

        var rejected = change.Approvals.Any(a => a.Decision == ApprovalDecision.Reject);

        // Distinct users who approved, minus the author when self-approval is off.
        var approverIds = change.Approvals
            .Where(a => a.Decision == ApprovalDecision.Approve)
            .Select(a => a.ApproverUserId)
            .Where(id => policy.AuthorCanApproveOwnChange || id != change.AuthorUserId)
            .Distinct()
            .ToList();

        var outstanding = new List<string>();

        var remainingTotal = policy.MinApprovals - approverIds.Count;
        if (remainingTotal > 0)
        {
            outstanding.Add($"{remainingTotal} more approval(s) needed");
        }

        foreach (var rule in policy.ApproverRules.Where(r => r.Mandatory))
        {
            var matching = approverIds.Count(id => matchesRule(id, rule));
            var need = Math.Max(rule.MinFromThisRule, rule.Type == ApproverRuleType.SpecificUser ? 1 : rule.MinFromThisRule);
            if (matching < need)
            {
                outstanding.Add(DescribeRule(rule, need - matching));
            }
        }

        var satisfied = !rejected
            && approverIds.Count >= policy.MinApprovals
            && outstanding.Count == 0;

        return new ApprovalEvaluation(
            Satisfied: satisfied,
            Rejected: rejected,
            ApprovalsCount: approverIds.Count,
            MinApprovals: policy.MinApprovals,
            Outstanding: outstanding);
    }

    private static string DescribeRule(ApproverRule rule, int stillNeeded) => rule.Type switch
    {
        ApproverRuleType.SpecificUser => "approval from a specific required user",
        ApproverRuleType.AnyFromRole => $"{stillNeeded} more from the required role",
        ApproverRuleType.AnyFromGroup => $"{stillNeeded} more from the required group",
        _ => "an additional required approval",
    };
}

/// <summary>Outcome of <see cref="ApprovalPolicyEvaluator.Evaluate"/>.</summary>
/// <param name="Satisfied">Whether the change can move to <see cref="ChangeStatus.Approved"/>.</param>
/// <param name="Rejected">Whether any approver rejected the change.</param>
/// <param name="ApprovalsCount">Distinct qualifying approvals counted.</param>
/// <param name="MinApprovals">The policy's minimum.</param>
/// <param name="Outstanding">Human-readable descriptions of what is still missing (for the dashboard).</param>
public sealed record ApprovalEvaluation(
    bool Satisfied,
    bool Rejected,
    int ApprovalsCount,
    int MinApprovals,
    IReadOnlyList<string> Outstanding);
