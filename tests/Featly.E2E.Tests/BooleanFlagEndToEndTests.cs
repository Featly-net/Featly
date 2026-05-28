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
/// Proves the M2 contract end-to-end: create a flag via the admin HTTP API,
/// trigger a polled refresh in the SDK, and confirm <see cref="IFlagClient"/>
/// returns the matching value.
/// </summary>
public class BooleanFlagEndToEndTests
{
    private const string AdminKey = "admin-key-e2e";
    private const string SdkKey = "sdk-key-e2e";

    [Fact]
    public async Task SDK_observes_a_flag_created_via_the_admin_API()
    {
        using var serverHost = await BuildServerHostAsync();
        var serverClient = serverHost.GetTestClient();
        var adminClient = CreateAuthorizedClient(serverHost, AdminKey);

        // Build the SDK against the TestServer's HttpMessageHandler.
        await using var sdkProvider = BuildSdkServices(serverHost.GetTestServer());
        var snapshotCache = sdkProvider.GetRequiredService<FeatlySnapshotCache>();
        var featly = sdkProvider.GetRequiredService<IFeatlyClient>();
        var http = sdkProvider.GetRequiredService<FeatlyHttpClient>();

        // 1. Create a boolean flag enabled with default variant "on".
        var createResponse = await adminClient.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "demo",
            name = "Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "on",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
        }, TestContext.Current.CancellationToken);
        createResponse.EnsureSuccessStatusCode();

        // 2. Manually drive one refresh (we don't want to wait the polling interval in a test).
        var fetched = await http.FetchConfigAsync(environmentKey: null, ifNoneMatch: null, ct: TestContext.Current.CancellationToken);
        fetched.Snapshot.Should().NotBeNull();
        snapshotCache.Replace(fetched.Snapshot!, fetched.Etag);

        // 3. The SDK should now see the flag.
        var enabled = await featly.Flags.IsEnabledAsync("demo", ct: TestContext.Current.CancellationToken);
        enabled.Should().BeTrue();

        // 4. Update the flag to flip the default variant to "off" and re-sync.
        //    This proves the snapshot is reloaded and the change propagates.
        await adminClient.PutAsJsonAsync("/api/admin/flags/demo", new
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
        }, TestContext.Current.CancellationToken);

        var refreshed = await http.FetchConfigAsync(environmentKey: null, ifNoneMatch: null, ct: TestContext.Current.CancellationToken);
        refreshed.Snapshot.Should().NotBeNull();
        snapshotCache.Replace(refreshed.Snapshot!, refreshed.Etag);

        var enabledAfter = await featly.Flags.IsEnabledAsync("demo", ct: TestContext.Current.CancellationToken);
        enabledAfter.Should().BeFalse("the default variant was switched to 'off' and the SDK re-fetched the snapshot");

        _ = serverClient; // suppress unused warning
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
        services.AddSingleton<IEventClient>(sp => new EventClient(
            new NullEventSink(),
            sp.GetRequiredService<IFeatlyContextAccessor>()));
        services.AddSingleton<IFeatlyClient>(sp => new FeatlyClient(
            sp.GetRequiredService<IFlagClient>(),
            sp.GetRequiredService<IConfigClient>(),
            sp.GetRequiredService<IEventClient>()));

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
