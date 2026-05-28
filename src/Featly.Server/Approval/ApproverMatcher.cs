using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Approval;

/// <summary>
/// Builds the <c>matchesRule</c> delegate that <see cref="ApprovalPolicyEvaluator"/>
/// needs, by pre-resolving each approver's role and group memberships from the
/// store. Membership for approval purposes is "holds the role / is in the group
/// anywhere" — not scoped to a project or environment.
/// </summary>
internal static class ApproverMatcher
{
    /// <summary>
    /// Resolves the role ids and group ids for each approver and returns a
    /// synchronous predicate suitable for <see cref="ApprovalPolicyEvaluator.Evaluate"/>.
    /// </summary>
    public static async Task<Func<Guid, ApproverRule, bool>> BuildAsync(
        StorageFacade store,
        IEnumerable<Guid> approverUserIds,
        CancellationToken ct)
    {
        var groupIdsByUser = new Dictionary<Guid, HashSet<Guid>>();
        var roleIdsByUser = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var userId in approverUserIds.Distinct())
        {
            var groups = await store.Groups.ListForMemberAsync(userId, ct).ConfigureAwait(false);
            var groupIds = groups.Select(g => g.Id).ToHashSet();
            groupIdsByUser[userId] = groupIds;

            // Assignee ids = the user plus every group it belongs to.
            var assigneeIds = new List<Guid>(groupIds) { userId };
            var assignments = await store.RoleAssignments.ListForAssigneesAsync(assigneeIds, ct).ConfigureAwait(false);
            roleIdsByUser[userId] = assignments.Select(a => a.RoleId).ToHashSet();
        }

        return (userId, rule) => rule.Type switch
        {
            ApproverRuleType.SpecificUser => rule.UserId == userId,
            ApproverRuleType.AnyFromRole => rule.RoleId is { } rid
                && roleIdsByUser.TryGetValue(userId, out var roles) && roles.Contains(rid),
            ApproverRuleType.AnyFromGroup => rule.GroupId is { } gid
                && groupIdsByUser.TryGetValue(userId, out var groups) && groups.Contains(gid),
            _ => false,
        };
    }
}
