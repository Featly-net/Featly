using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Authentication;
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
/// End-to-end coverage for issue #193: the <see cref="PermissionFilter"/> now
/// resolves the request's environment (from <c>?env=</c>, else the default) and
/// passes it to the permission checker, so an environment-scoped role assignment
/// is honored — granting access in its environment and denying it in others.
/// </summary>
public class PermissionFilterEnvScopeTests
{
    private static readonly object FlagBody = new
    {
        key = "demo",
        name = "Demo",
        type = "Boolean",
        enabled = true,
        defaultVariantKey = "off",
        variants = new[]
        {
            new { key = "on", name = "On", value = true },
            new { key = "off", name = "Off", value = false },
        },
    };

    [Fact]
    public async Task Env_scoped_editor_can_write_in_its_environment_but_not_another()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;

        var project = await store.Projects.GetDefaultAsync(ct);
        var defaultEnv = await store.Environments.GetDefaultAsync(project!.Id, ct);
        var staging = await CreateEnvironmentAsync(store, project.Id, "staging", ct);

        // Erin is Editor, but scoped to the default environment only.
        var erin = await CreateUserAsync(store, "erin@example.com", ct);
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = erin.Id,
            ProjectId = project.Id,
            EnvironmentId = defaultEnv!.Id, // scoped to the default env
            RoleId = editor!.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var token = await MintAdminKeyBoundToAsync(host, erin.Id, defaultEnv.Id, ct);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Default environment (Erin's scope): the create is authorized.
        var inScope = await client.PostAsJsonAsync("/api/admin/flags", FlagBody, ct);
        inScope.StatusCode.Should().Be(HttpStatusCode.Created);

        // A different environment: the env-scoped Editor grant does not apply, so
        // the Open-mode Viewer floor governs — and Viewer cannot create flags.
        var outOfScope = await client.PostAsJsonAsync(
            new Uri($"/api/admin/flags?env={staging.Key}", UriKind.Relative), FlagBody, ct);
        outOfScope.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // An unknown environment resolves to no environment; the env-scoped grant
        // still doesn't apply, so the Viewer floor denies the write as well.
        var unknownEnv = await client.PostAsJsonAsync(
            new Uri("/api/admin/flags?env=does-not-exist", UriKind.Relative), FlagBody, ct);
        unknownEnv.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Wildcard_editor_can_write_in_any_environment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;

        var project = await store.Projects.GetDefaultAsync(ct);
        var staging = await CreateEnvironmentAsync(store, project!.Id, "staging", ct);

        // Frank is Editor project-wide (wildcard environment).
        var frank = await CreateUserAsync(store, "frank@example.com", ct);
        var editor = await store.Roles.GetByKeyAsync(SystemRoles.EditorKey, ct);
        await store.RoleAssignments.CreateAsync(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            AssigneeType = AssigneeType.User,
            AssigneeId = frank.Id,
            ProjectId = project.Id,
            EnvironmentId = null, // wildcard: every environment in the project
            RoleId = editor!.Id,
            AssignedAt = DateTimeOffset.UtcNow,
        }, ct);

        var defaultEnv = await store.Environments.GetDefaultAsync(project.Id, ct);
        var token = await MintAdminKeyBoundToAsync(host, frank.Id, defaultEnv!.Id, ct);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Wildcard grant still matches a concrete environment (this only tightens,
        // never broadens): both the default and staging environments are writable.
        (await client.PostAsJsonAsync("/api/admin/flags", FlagBody, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync(
            new Uri($"/api/admin/flags?env={staging.Key}", UriKind.Relative), FlagBody, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<Featly.Environment> CreateEnvironmentAsync(StorageFacade store, Guid projectId, string key, CancellationToken ct)
    {
        var env = new Featly.Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Key = key,
            Name = key,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.Environments.CreateAsync(env, ct);
        return env;
    }

    private static async Task<User> CreateUserAsync(StorageFacade store, string identifier, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await store.Users.UpsertAsync(new User
        {
            Id = Guid.NewGuid(),
            Identifier = identifier,
            DisplayName = identifier,
            Email = identifier,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "test",
            UpdatedBy = "test",
        }, "test", ct);
        return (await store.Users.GetByIdentifierAsync(identifier, ct))!;
    }

    private static async Task<string> MintAdminKeyBoundToAsync(IHost host, Guid userId, Guid environmentId, CancellationToken ct)
    {
        var store = host.Services.GetRequiredService<StorageFacade>();
        var hasher = host.Services.GetRequiredService<ApiKeyHasher>();
        var plaintext = hasher.GenerateToken();
        await store.ApiKeys.CreateAsync(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "erin-key",
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = ApiKeyScope.AdminWrite,
            EnvironmentId = environmentId,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        }, ct);
        return plaintext;
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
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
