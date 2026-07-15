using System.Net.Http.Headers;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Boots a Featly server over <c>TestServer</c> with the in-memory store — the
/// one place the test suite assembles a host (issue #225). Every test file used
/// to hand-roll this; they now call <see cref="CreateAsync"/> and pass only what
/// they actually vary.
/// </summary>
internal static class FeatlyTestHost
{
    /// <summary>The static bootstrap admin key the default host is configured with.</summary>
    public const string AdminKey = "admin-key-test";

    /// <summary>The static SDK key the default host is configured with.</summary>
    public const string SdkKey = "sdk-key-test";

    /// <summary>
    /// Starts a host wired for the admin + SDK APIs.
    /// </summary>
    /// <param name="settings">
    /// Extra <c>appsettings</c> entries, merged over the defaults (and able to
    /// override them).
    /// </param>
    /// <param name="configureServices">Extra DI registrations, applied after Featly's own.</param>
    /// <param name="withStaticApiKeys">
    /// When <c>false</c>, the static <c>AdminApiKey</c> / <c>SdkApiKey</c> are left
    /// unset — for tests that exercise persisted, environment-scoped API keys and
    /// must not have a wildcard bootstrap key accepted first.
    /// </param>
    public static async Task<IHost> CreateAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null,
        bool withStaticApiKeys = true)
    {
        var config = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (withStaticApiKeys)
        {
            config["Featly:Server:AdminApiKey"] = AdminKey;
            config["Featly:Server:SdkApiKey"] = SdkKey;
        }

        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                config[key] = value;
            }
        }

        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config));
                web.ConfigureServices(services =>
                {
                    services.AddFeatlyInMemoryStore();
                    services.AddFeatlyServer();
                    services.AddRouting();
                    configureServices?.Invoke(services);
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

    /// <summary>A client bearing the static admin key.</summary>
    public static HttpClient AdminClient(this IHost host) => BearerClient(host, AdminKey);

    /// <summary>A client bearing the static SDK key.</summary>
    public static HttpClient SdkClient(this IHost host) => BearerClient(host, SdkKey);

    private static HttpClient BearerClient(IHost host, string key)
    {
        ArgumentNullException.ThrowIfNull(host);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }
}
