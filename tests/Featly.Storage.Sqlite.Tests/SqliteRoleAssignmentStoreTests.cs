using AwesomeAssertions;
using Xunit;

namespace Featly.Storage.Sqlite.Tests;

/// <summary>
/// Round-trips for the SQLite-backed role assignment store. Assignments are the
/// polymorphic join (user/group → role) scoped to a project and optionally an
/// environment. The permission checker's hot path is
/// <c>ListForAssigneesAsync</c>, so the batch-by-assignee lookup gets the most
/// coverage.
/// </summary>
public class SqliteRoleAssignmentStoreTests
{
    [Fact]
    public async Task Create_then_get_round_trips_all_fields()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

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
        await host.Store.RoleAssignments.CreateAsync(assignment, ct);

        var loaded = await host.Store.RoleAssignments.GetByIdAsync(assignment.Id, ct);
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
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

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
        await host.Store.RoleAssignments.CreateAsync(assignment, ct);

        var loaded = await host.Store.RoleAssignments.GetByIdAsync(assignment.Id, ct);
        loaded!.EnvironmentId.Should().BeNull();
        loaded.AssigneeType.Should().Be(AssigneeType.Group);
        loaded.AssignedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task ListForAssignees_returns_only_matching_assignees()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var group = Guid.NewGuid();
        var project = Guid.NewGuid();

        await host.Store.RoleAssignments.CreateAsync(NewAssignment(alice, project), ct);
        await host.Store.RoleAssignments.CreateAsync(NewAssignment(alice, project), ct);
        await host.Store.RoleAssignments.CreateAsync(NewAssignment(bob, project), ct);
        await host.Store.RoleAssignments.CreateAsync(NewAssignment(group, project, AssigneeType.Group), ct);

        // Alice's own id plus the group she belongs to.
        var forAlicePlusGroup = await host.Store.RoleAssignments.ListForAssigneesAsync([alice, group], ct);
        forAlicePlusGroup.Should().HaveCount(3);
        forAlicePlusGroup.Select(a => a.AssigneeId).Should().OnlyContain(id => id == alice || id == group);

        var forBob = await host.Store.RoleAssignments.ListForAssigneesAsync([bob], ct);
        forBob.Should().ContainSingle();
    }

    [Fact]
    public async Task ListForAssignees_empty_input_returns_empty()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var result = await host.Store.RoleAssignments.ListForAssigneesAsync([], ct);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListForProject_scopes_by_project()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        await host.Store.RoleAssignments.CreateAsync(NewAssignment(Guid.NewGuid(), projectA), ct);
        await host.Store.RoleAssignments.CreateAsync(NewAssignment(Guid.NewGuid(), projectA), ct);
        await host.Store.RoleAssignments.CreateAsync(NewAssignment(Guid.NewGuid(), projectB), ct);

        (await host.Store.RoleAssignments.ListForProjectAsync(projectA, ct)).Should().HaveCount(2);
        (await host.Store.RoleAssignments.ListForProjectAsync(projectB, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        await using var host = await SqliteTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var assignment = NewAssignment(Guid.NewGuid(), Guid.NewGuid());
        await host.Store.RoleAssignments.CreateAsync(assignment, ct);

        await host.Store.RoleAssignments.DeleteAsync(assignment.Id, ct);
        (await host.Store.RoleAssignments.GetByIdAsync(assignment.Id, ct)).Should().BeNull();

        // Second delete on a missing id is a no-op, not a throw.
        await host.Store.RoleAssignments.DeleteAsync(assignment.Id, ct);
        await host.Store.RoleAssignments.DeleteAsync(Guid.NewGuid(), ct);
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
