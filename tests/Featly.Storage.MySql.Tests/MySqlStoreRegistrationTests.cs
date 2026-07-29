using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.MySql.Tests;

/// <summary>
/// Covers the consumer-facing entry point (issue #276, mirroring #274/#179):
/// <c>AddFeatlyMySqlStore()</c> must resolve a fully wired
/// <see cref="StorageFacade"/> that actually talks to MySQL — the piece that
/// turns 19 working sub-stores into a usable provider.
/// </summary>
[Trait("Category", "RequiresMySql")]
public class MySqlStoreRegistrationTests
{
    [Fact]
    public async Task AddFeatlyMySqlStore_resolves_a_facade_that_round_trips()
    {
        await using var host = await MySqlTestHost.CreateAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var services = new ServiceCollection();
        services.AddLogging();
        // A real host supplies IConfiguration; BindConfiguration needs it.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddFeatlyMySqlStore(o => o.ConnectionString = host.ConnectionString);
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<StorageFacade>();

        // Every sub-store is wired — a facade with a null property would only
        // surface as a NullReferenceException deep in a request.
        store.Flags.Should().NotBeNull();
        store.Projects.Should().NotBeNull();
        store.Environments.Should().NotBeNull();
        store.Segments.Should().NotBeNull();
        store.Configs.Should().NotBeNull();
        store.Users.Should().NotBeNull();
        store.Roles.Should().NotBeNull();
        store.RoleAssignments.Should().NotBeNull();
        store.Groups.Should().NotBeNull();
        store.RoleUpgradeRequests.Should().NotBeNull();
        store.PendingChanges.Should().NotBeNull();
        store.ApprovalPolicies.Should().NotBeNull();
        store.Experiments.Should().NotBeNull();
        store.Events.Should().NotBeNull();
        store.Assignments.Should().NotBeNull();
        store.Webhooks.Should().NotBeNull();
        store.WebhookDeliveries.Should().NotBeNull();
        store.Audit.Should().NotBeNull();
        store.ApiKeys.Should().NotBeNull();
        store.Settings.Should().NotBeNull();
        store.Changes.Should().NotBeNull();

        // And it reaches the real database through the facade, not just DI.
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Key = "facade",
            Name = "Facade",
            IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.Projects.CreateAsync(project, ct);

        var environment = new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Key = "production",
            Name = "Production",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.Environments.CreateAsync(environment, ct);

        (await store.Projects.GetDefaultAsync(ct))!.Key.Should().Be("facade");
        (await store.Environments.GetByKeyAsync(project.Id, "production", ct))!.Name.Should().Be("Production");

        // The facade is a singleton — the server resolves it once per host.
        provider.GetRequiredService<StorageFacade>().Should().BeSameAs(store);
    }

    [Fact]
    public void AddFeatlyMySqlStore_without_a_connection_string_fails_fast()
    {
        // There is no sensible default server, so this must fail at startup with
        // a clear message rather than at the first query with a MySqlException.
        var services = new ServiceCollection();
        services.AddLogging();
        // A real host supplies IConfiguration; BindConfiguration needs it.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddFeatlyMySqlStore();
        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IOptions<MySqlFeatlyStoreOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .WithMessage("*connection string is required*");
    }
}
