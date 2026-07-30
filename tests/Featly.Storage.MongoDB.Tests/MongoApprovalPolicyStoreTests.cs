using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.MongoDB.Tests;

[Trait("Category", "RequiresMongoDB")]
public class MongoApprovalPolicyStoreTests
{
    [Fact]
    public async Task Upsert_then_get_round_trips_with_approver_rules()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApprovalPolicyStore;
        var envId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var policy = new ApprovalPolicy
        {
            Id = Guid.NewGuid(),
            EnvironmentId = envId,
            Required = true,
            MinApprovals = 2,
            ApproverRules =
            [
                new ApproverRule { Id = Guid.NewGuid(), Type = ApproverRuleType.AnyFromRole, RoleId = roleId, Mandatory = true, MinFromThisRule = 1 },
            ],
        };
        await store.UpsertAsync(policy, ct);

        var loaded = await store.GetByEnvironmentAsync(envId, ct);
        loaded.Should().NotBeNull();
        loaded!.Required.Should().BeTrue();
        loaded.MinApprovals.Should().Be(2);
        loaded.ApproverRules.Should().ContainSingle().Which.RoleId.Should().Be(roleId);
    }

    [Fact]
    public async Task Upsert_replaces_in_place_keeping_id()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApprovalPolicyStore;
        var envId = Guid.NewGuid();

        var policy = new ApprovalPolicy { Id = Guid.NewGuid(), EnvironmentId = envId, Required = false, MinApprovals = 1 };
        await store.UpsertAsync(policy, ct);
        var originalId = (await store.GetByEnvironmentAsync(envId, ct))!.Id;

        await store.UpsertAsync(new ApprovalPolicy
        {
            Id = Guid.NewGuid(), // ignored on update — environment is the natural key
            EnvironmentId = envId,
            Required = true,
            MinApprovals = 3,
        }, ct);

        var loaded = await store.GetByEnvironmentAsync(envId, ct);
        loaded!.Id.Should().Be(originalId);
        loaded.Required.Should().BeTrue();
        loaded.MinApprovals.Should().Be(3);
    }

    [Fact]
    public async Task DeleteByEnvironment_is_idempotent()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.ApprovalPolicyStore;
        var envId = Guid.NewGuid();

        await store.UpsertAsync(new ApprovalPolicy { Id = Guid.NewGuid(), EnvironmentId = envId, Required = true }, ct);
        await store.DeleteByEnvironmentAsync(envId, ct);
        (await store.GetByEnvironmentAsync(envId, ct)).Should().BeNull();

        // Second delete on a missing environment is a no-op.
        await store.DeleteByEnvironmentAsync(envId, ct);
        await store.DeleteByEnvironmentAsync(Guid.NewGuid(), ct);
    }

    [Fact]
    public async Task Get_returns_null_when_environment_has_no_policy()
    {
        await using var host = await MongoTestHost.CreateAsync(TestContext.Current.CancellationToken);
        (await host.ApprovalPolicyStore.GetByEnvironmentAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
