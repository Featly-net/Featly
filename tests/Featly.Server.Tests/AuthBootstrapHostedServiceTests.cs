using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Tests;

/// <summary>
/// Verifies the M6 PR 6C bootstrap pass: AuthBootstrapHostedService seeds
/// the four system roles on every boot, and provisions the bootstrap admin
/// user when <c>Featly:Authorization:BootstrapAdminIdentifier</c> is set.
/// </summary>
public class AuthBootstrapHostedServiceTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task System_roles_are_seeded_on_boot()
    {
        using var host = await BuildHostAsync(bootstrapAdmin: "");
        var store = host.Services.GetRequiredService<StorageFacade>();

        var roles = await store.Roles.ListAsync(TestContext.Current.CancellationToken);
        roles.Should().HaveCount(4);
        roles.Select(r => r.Key).Should().BeEquivalentTo([SystemRoles.ViewerKey, SystemRoles.EditorKey, SystemRoles.ApproverKey, SystemRoles.AdminKey]);
        roles.Should().AllSatisfy(r => r.IsSystem.Should().BeTrue());
    }

    [Fact]
    public async Task Bootstrap_admin_is_created_when_identifier_is_configured()
    {
        using var host = await BuildHostAsync(bootstrapAdmin: "alice@example.com");
        var store = host.Services.GetRequiredService<StorageFacade>();

        var alice = await store.Users.GetByIdentifierAsync("alice@example.com", TestContext.Current.CancellationToken);
        alice.Should().NotBeNull();
        alice!.Email.Should().Be("alice@example.com");
        alice.Disabled.Should().BeFalse();
        alice.CreatedBy.Should().Be("bootstrap");
    }

    [Fact]
    public async Task Bootstrap_admin_is_idempotent_across_reseed()
    {
        // Re-running the host with the same identifier should reuse the
        // existing user row. We simulate that by upserting the same identifier
        // again through the public store API after the first boot.
        using var host = await BuildHostAsync(bootstrapAdmin: "alice@example.com");
        var store = host.Services.GetRequiredService<StorageFacade>();

        var first = await store.Users.GetByIdentifierAsync("alice@example.com", TestContext.Current.CancellationToken);
        first.Should().NotBeNull();

        var ct = TestContext.Current.CancellationToken;
        await store.Users.UpsertAsync(first!, "bootstrap", ct);

        var second = await store.Users.GetByIdentifierAsync("alice@example.com", ct);
        second!.Id.Should().Be(first!.Id);
    }

    [Fact]
    public async Task Bootstrap_admin_skipped_when_identifier_empty()
    {
        using var host = await BuildHostAsync(bootstrapAdmin: "");
        var store = host.Services.GetRequiredService<StorageFacade>();

        var users = await store.Users.ListAsync(TestContext.Current.CancellationToken);
        users.Should().BeEmpty();
    }

    [Fact]
    public async Task Bootstrap_admin_acts_as_admin_role_for_permission_checks()
    {
        using var host = await BuildHostAsync(bootstrapAdmin: "alice@example.com");
        // The legacy admin api-key already gives admin access; this asserts the
        // bootstrap identifier maps to the admin role through DefaultFeatlyPermissionChecker.
        var checker = host.Services.GetRequiredService<Featly.Authorization.IFeatlyPermissionChecker>();

        var resolved = new Featly.Authorization.ResolvedUser("alice@example.com", "Alice");
        var canCreate = await checker.HasAsync(resolved, Guid.Empty, null, Permission.FlagCreate, TestContext.Current.CancellationToken);
        canCreate.Should().BeTrue();

        var resolvedStranger = new Featly.Authorization.ResolvedUser("bob@example.com", "Bob");
        var bobCanCreate = await checker.HasAsync(resolvedStranger, Guid.Empty, null, Permission.FlagCreate, TestContext.Current.CancellationToken);
        bobCanCreate.Should().BeFalse("only the bootstrap identifier is mapped to admin; everyone else gets viewer");

        var bobCanRead = await checker.HasAsync(resolvedStranger, Guid.Empty, null, Permission.FlagRead, TestContext.Current.CancellationToken);
        bobCanRead.Should().BeTrue("non-bootstrap users get the viewer role");
    }

    private static async Task<IHost> BuildHostAsync(string bootstrapAdmin)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Featly:Server:AdminApiKey"] = AdminKey,
                    ["Featly:Server:SdkApiKey"] = SdkKey,
                    ["Featly:Authorization:BootstrapAdminIdentifier"] = bootstrapAdmin,
                }));
                web.ConfigureServices(services =>
                {
                    services.AddFeatlyInMemoryStore();
                    services.AddFeatlyServer();
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapFeatlyApi());
                });
            });
        return await builder.StartAsync(TestContext.Current.CancellationToken);
    }
}
