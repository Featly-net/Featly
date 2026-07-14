using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server.Authentication;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Server.Tests;

/// <summary>
/// Regression coverage for the API-key environment-scope enforcement (ADR-0009,
/// issue #188): a persisted <c>SdkRead</c> key bound to one environment must not
/// read another environment's snapshot by changing the <c>?env=</c> query param.
/// </summary>
public class SdkEnvironmentScopeTests
{
    [Fact]
    public async Task Persisted_sdk_key_is_denied_a_different_environment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;

        var (token, _) = await MintSdkKeyForDefaultEnvironmentAsync(host, ct);
        var otherEnv = await CreateEnvironmentAsync(store, "staging", ct);

        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await sdk.GetAsync(
            new Uri($"/api/sdk/config?env={otherEnv.Key}", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Persisted_sdk_key_reads_its_own_environment()
    {
        using var host = await BuildHostAsync();
        var ct = TestContext.Current.CancellationToken;

        var (token, envKey) = await MintSdkKeyForDefaultEnvironmentAsync(host, ct);

        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Explicit env matching the binding.
        var explicitEnv = await sdk.GetAsync(
            new Uri($"/api/sdk/config?env={envKey}", UriKind.Relative), ct);
        explicitEnv.StatusCode.Should().Be(HttpStatusCode.OK);

        // Omitted env resolves to the key's own environment, not a cross-env default.
        var implicitEnv = await sdk.GetAsync(new Uri("/api/sdk/config", UriKind.Relative), ct);
        implicitEnv.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Persisted_sdk_key_stream_is_denied_a_different_environment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;

        var (token, _) = await MintSdkKeyForDefaultEnvironmentAsync(host, ct);
        var otherEnv = await CreateEnvironmentAsync(store, "staging", ct);

        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await sdk.GetAsync(
            new Uri($"/api/sdk/stream?env={otherEnv.Key}", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Persisted_sdk_key_events_are_scoped_to_its_environment()
    {
        using var host = await BuildHostAsync();
        var store = host.Services.GetRequiredService<StorageFacade>();
        var ct = TestContext.Current.CancellationToken;

        var (token, envKey) = await MintSdkKeyForDefaultEnvironmentAsync(host, ct);
        var otherEnv = await CreateEnvironmentAsync(store, "staging", ct);

        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var batch = new
        {
            events = new[]
            {
                new { type = "Custom", subjectKey = "u1", customKey = "checkout.completed" },
            },
        };

        // Cross-environment ingest is rejected.
        var denied = await sdk.PostAsJsonAsync(
            new Uri($"/api/sdk/events?env={otherEnv.Key}", UriKind.Relative), batch, ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Ingest into the key's own environment is accepted.
        var allowed = await sdk.PostAsJsonAsync(
            new Uri($"/api/sdk/events?env={envKey}", UriKind.Relative), batch, ct);
        allowed.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    private static async Task<(string Token, string EnvKey)> MintSdkKeyForDefaultEnvironmentAsync(IHost host, CancellationToken ct)
    {
        var store = host.Services.GetRequiredService<StorageFacade>();
        var hasher = host.Services.GetRequiredService<ApiKeyHasher>();

        var project = await store.Projects.GetDefaultAsync(ct);
        var env = await store.Environments.GetDefaultAsync(project!.Id, ct);

        var plaintext = hasher.GenerateToken();
        await store.ApiKeys.CreateAsync(new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = "sdk-prod",
            Prefix = ApiKeyHasher.ExtractPrefix(plaintext),
            Hash = hasher.Hash(plaintext),
            Scope = ApiKeyScope.SdkRead,
            EnvironmentId = env!.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "test",
        }, ct);

        return (plaintext, env.Key);
    }

    private static async Task<Featly.Environment> CreateEnvironmentAsync(StorageFacade store, string key, CancellationToken ct)
    {
        var project = await store.Projects.GetDefaultAsync(ct);
        var env = new Featly.Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = project!.Id,
            Key = key,
            Name = key,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await store.Environments.CreateAsync(env, ct);
        return env;
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
