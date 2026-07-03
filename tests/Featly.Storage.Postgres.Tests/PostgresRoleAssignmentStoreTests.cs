using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Postgres.Tests;

/// <summary>
/// Round-trips for <c>PostgresRoleAssignmentStore</c>, mirroring
/// <c>SqliteRoleAssignmentStoreTests</c>. Assignments are the polymorphic join
/// (user/group → role) scoped to a project and optionally an environment.
/// </summary>
[Trait("Category", "RequiresPostgres")]
public class PostgresRoleAssignmentStoreTests
{
    [Fact]
    public async Task Create_then_get_round_trips_all_fields()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleAssignmentStore;

        var assignment = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            EnvironmentId = Guid.NewGuid(),
            RoleId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = Guid.NewGuid(),
        };
        await store.CreateAsync(assignment, ct);

        var loaded = await store.GetByIdAsync(assignment.Id, ct);
        loaded.Should().NotBeNull();
        loaded!.AssigneeType.Should().Be(AssigneeType.User);
        loaded.AssigneeId.Should().Be(assignment.AssigneeId);
        loaded.ProjectId.Should().Be(assignment.ProjectId);
        loaded.EnvironmentId.Should().Be(assignment.EnvironmentId);
        loaded.RoleId.Should().Be(assignment.RoleId);
        loaded.AssignedByUserId.Should().Be(assignment.AssignedByUserId);
    }

    [Fact]
    public async Task Wildcard_environment_persists_as_null()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleAssignmentStore;

        var assignment = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.Group,
            AssigneeId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            EnvironmentId = null,
            RoleId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow,
        };
        await store.CreateAsync(assignment, ct);

        var loaded = await store.GetByIdAsync(assignment.Id, ct);
        loaded!.EnvironmentId.Should().BeNull();
        loaded.AssigneeType.Should().Be(AssigneeType.Group);
        loaded.AssignedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task ListForAssignees_returns_only_matching_assignees()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleAssignmentStore;

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var group = Guid.NewGuid();
        var project = Guid.NewGuid();

        await store.CreateAsync(NewAssignment(alice, project), ct);
        await store.CreateAsync(NewAssignment(alice, project), ct);
        await store.CreateAsync(NewAssignment(bob, project), ct);
        await store.CreateAsync(NewAssignment(group, project, AssigneeType.Group), ct);

        var forAlicePlusGroup = await store.ListForAssigneesAsync([alice, group], ct);
        forAlicePlusGroup.Should().HaveCount(3);
        forAlicePlusGroup.Select(a => a.AssigneeId).Should().OnlyContain(id => id == alice || id == group);

        var forBob = await store.ListForAssigneesAsync([bob], ct);
        forBob.Should().ContainSingle();
    }

    [Fact]
    public async Task ListForAssignees_empty_input_returns_empty()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleAssignmentStore;

        var result = await store.ListForAssigneesAsync([], ct);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListForProject_scopes_by_project()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleAssignmentStore;

        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        await store.CreateAsync(NewAssignment(Guid.NewGuid(), projectA), ct);
        await store.CreateAsync(NewAssignment(Guid.NewGuid(), projectA), ct);
        await store.CreateAsync(NewAssignment(Guid.NewGuid(), projectB), ct);

        (await store.ListForProjectAsync(projectA, ct)).Should().HaveCount(2);
        (await store.ListForProjectAsync(projectB, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        await using var host = await PostgresTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var store = host.RoleAssignmentStore;

        var assignment = NewAssignment(Guid.NewGuid(), Guid.NewGuid());
        await store.CreateAsync(assignment, ct);

        await store.DeleteAsync(assignment.Id, ct);
        (await store.GetByIdAsync(assignment.Id, ct)).Should().BeNull();

        // Second delete on a missing id is a no-op, not a throw.
        await store.DeleteAsync(assignment.Id, ct);
        await store.DeleteAsync(Guid.NewGuid(), ct);
    }

    private static RoleAssignment NewAssignment(Guid assigneeId, Guid projectId, AssigneeType type = AssigneeType.User)
        => new()
        {
            Id = Guid.NewGuid(),
            AssigneeType = type,
            AssigneeId = assigneeId,
            ProjectId = projectId,
            EnvironmentId = null,
            RoleId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow,
        };
}
