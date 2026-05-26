using System.Net.Http.Headers;
using System.Net.Http.Json;
using Featly;
using Featly.Sdk;
using Featly.Sdk.Internal;
using Featly.Server;
using Featly.Storage.InMemory;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.E2E.Tests;

/// <summary>
/// End-to-end proof for M4 PR 4B: create a dynamic config via the admin HTTP
/// API, poll the SDK snapshot, and verify <see cref="IConfigClient"/> serves
/// the value with the same targeting semantics flags already have.
/// </summary>
public class ConfigEndToEndTests
{
    private const string AdminKey = "admin-key-e2e-config";
    private const string SdkKey = "sdk-key-e2e-config";

    [Fact]
    public async Task SDK_observes_a_config_created_via_the_admin_API()
    {
        using var serverHost = await BuildServerHostAsync();
        var adminClient = CreateAuthorizedClient(serverHost, AdminKey);

        await using var sdkProvider = BuildSdkServices(serverHost.GetTestServer());
        var snapshotCache = sdkProvider.GetRequiredService<FeatlySnapshotCache>();
        var featly = sdkProvider.GetRequiredService<IFeatlyClient>();
        var http = sdkProvider.GetRequiredService<FeatlyHttpClient>();

        // 1. Create a typed config with a default value of 30 seconds.
        var createResponse = await adminClient.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "checkout.timeout",
            name = "Checkout Timeout",
            type = "Int",
            defaultValue = 30,
        }, TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();

        // 2. Refresh the snapshot once (no waiting on polling).
        var fetched = await http.FetchConfigAsync(environmentKey: null, ifNoneMatch: null, ct: TestContext.Current.CancellationToken);
        fetched.Snapshot.Should().NotBeNull();
        snapshotCache.Replace(fetched.Snapshot!, fetched.Etag);

        // 3. SDK sees the default value.
        var seconds = await featly.Configs.GetAsync("checkout.timeout", defaultValue: 0, ct: TestContext.Current.CancellationToken);
        seconds.Should().Be(30);

        // 4. Update the config to add a BR rule -> 60s, leaving the default at 30.
        await adminClient.PutAsJsonAsync("/api/admin/configs/checkout.timeout", new
        {
            key = "checkout.timeout",
            name = "Checkout Timeout",
            type = "Int",
            defaultValue = 30,
            rules = new[]
            {
                new
                {
                    order = 0,
                    name = "BR",
                    conditions = new[]
                    {
                        new { attribute = "user.country", @operator = "Equals", value = (object)"BR" },
                    },
                    value = (object)60,
                },
            },
        }, TestContext.Current.CancellationToken);

        var refreshed = await http.FetchConfigAsync(environmentKey: null, ifNoneMatch: null, ct: TestContext.Current.CancellationToken);
        refreshed.Snapshot.Should().NotBeNull();
        snapshotCache.Replace(refreshed.Snapshot!, refreshed.Etag);

        // 5. BR users now get 60s; everyone else still gets 30s.
        var brCtx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "BR" });
        var brSeconds = await featly.Configs.GetAsync("checkout.timeout", defaultValue: 0, brCtx, TestContext.Current.CancellationToken);
        brSeconds.Should().Be(60, "BR rule matches");

        var usCtx = new EvaluationContext(Attributes: new Dictionary<string, object?> { ["user.country"] = "US" });
        var usSeconds = await featly.Configs.GetAsync("checkout.timeout", defaultValue: 0, usCtx, TestContext.Current.CancellationToken);
        usSeconds.Should().Be(30, "US falls through to the default");
    }

    [Fact]
    public async Task SDK_sees_NotFound_for_a_config_not_yet_synced()
    {
        using var serverHost = await BuildServerHostAsync();
        await using var sdkProvider = BuildSdkServices(serverHost.GetTestServer());
        var featly = sdkProvider.GetRequiredService<IFeatlyClient>();

        var result = await featly.Configs.EvaluateAsync(
            "does.not.exist",
            defaultValue: "fallback",
            ct: TestContext.Current.CancellationToken);

        result.Value.Should().Be("fallback");
        result.Reason.Should().Be(EvaluationReason.NotFound);
    }

    private static HttpClient CreateAuthorizedClient(IHost host, string bearer)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static async Task<IHost> BuildServerHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Featly:Server:AdminApiKey"] = AdminKey,
                    ["Featly:Server:SdkApiKey"] = SdkKey,
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

        var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private sealed class E2ENoOpAccessor : IFeatlyContextAccessor
    {
        public EvaluationContext? Current => null;
    }

    private static ServiceProvider BuildSdkServices(TestServer server)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<FeatlySnapshotCache>();
        services.AddSingleton<IFeatlyContextAccessor, E2ENoOpAccessor>();
        services.AddSingleton<IFlagClient>(sp => new FlagClient(
            sp.GetRequiredService<FeatlySnapshotCache>(),
            sp.GetRequiredService<IFeatlyContextAccessor>()));
        services.AddSingleton<IConfigClient>(sp => new ConfigClient(
            sp.GetRequiredService<FeatlySnapshotCache>(),
            sp.GetRequiredService<IFeatlyContextAccessor>()));
        services.AddSingleton<IFeatlyClient>(sp => new FeatlyClient(
            sp.GetRequiredService<IFlagClient>(),
            sp.GetRequiredService<IConfigClient>()));

        services.AddHttpClient<FeatlyHttpClient>(client =>
        {
            client.BaseAddress = server.BaseAddress;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);
        }).ConfigurePrimaryHttpMessageHandler(() => server.CreateHandler());

        services.Configure<FeatlySdkOptions>(opts =>
        {
            opts.ServerUrl = server.BaseAddress;
            opts.ApiKey = SdkKey;
            opts.EnableStreaming = false;
            opts.PollingInterval = TimeSpan.FromSeconds(5);
        });

        return services.BuildServiceProvider();
    }
}
