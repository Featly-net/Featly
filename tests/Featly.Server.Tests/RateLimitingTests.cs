using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server.Endpoints;
using Featly.Server.Settings;
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
/// Opt-in request throttling over the Featly HTTP surface (SECURITY_AUDIT.md
/// follow-up). Fixed one-minute windows partitioned per surface and per client
/// (identity when authenticated, else IP); disabled by default; limits are
/// DB-overridable through the settings subsystem.
/// </summary>
public class RateLimitingTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    private static readonly Uri LoginUri = new("/api/auth/login", UriKind.Relative);
    private static readonly Uri FlagsUri = new("/api/admin/flags", UriKind.Relative);

    [Fact]
    public async Task Disabled_by_default_never_throttles()
    {
        using var host = await BuildHostAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        for (var i = 0; i < 30; i++)
        {
            (await admin.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Auth_surface_throttles_failed_logins()
    {
        using var host = await BuildHostAsync(new()
        {
            ["Featly:RateLimiting:Enabled"] = "true",
            ["Featly:RateLimiting:AuthPermitsPerMinute"] = "3",
        });
        var ct = TestContext.Current.CancellationToken;
        var anon = host.GetTestClient();

        // The first three attempts hit the endpoint (wrong key -> 401)...
        for (var i = 0; i < 3; i++)
        {
            (await anon.PostAsJsonAsync(LoginUri, new { apiKey = "wrong" }, ct))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ...the fourth is throttled before the handler runs.
        var throttled = await anon.PostAsJsonAsync(LoginUri, new { apiKey = "wrong" }, ct);
        throttled.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        throttled.Headers.TryGetValues("Retry-After", out var retryAfter).Should().BeTrue();
        retryAfter!.Single().Should().Be("60");
    }

    [Fact]
    public async Task Admin_surface_throttles_per_identity_with_its_own_limit()
    {
        using var host = await BuildHostAsync(new()
        {
            ["Featly:RateLimiting:Enabled"] = "true",
            ["Featly:RateLimiting:AuthPermitsPerMinute"] = "1",
            ["Featly:RateLimiting:AdminPermitsPerMinute"] = "5",
        });
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        // The admin surface has its own budget, independent of the auth surface.
        for (var i = 0; i < 5; i++)
        {
            (await admin.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        (await admin.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Zero_means_unlimited_for_that_surface()
    {
        using var host = await BuildHostAsync(new()
        {
            ["Featly:RateLimiting:Enabled"] = "true",
            ["Featly:RateLimiting:AdminPermitsPerMinute"] = "0",
        });
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        for (var i = 0; i < 30; i++)
        {
            (await admin.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Settings_api_overrides_and_takes_effect_without_restart()
    {
        using var host = await BuildHostAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        // Effective default: disabled, hardcoded source.
        var before = await admin.GetFromJsonAsync<SettingView<FeatlyRateLimitSettings>>(
            "/api/admin/settings/rate-limit", TestJson.Options, ct);
        before!.Value.Enabled.Should().BeFalse();
        before.Source.Should().Be(nameof(FeatlySettingsSource.HardcodedDefault));

        // Enable with a tiny admin budget through the settings API (DB layer).
        var put = await admin.PutAsJsonAsync("/api/admin/settings/rate-limit", new FeatlyRateLimitSettings
        {
            Enabled = true,
            AdminPermitsPerMinute = 3,
        }, ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await put.Content.ReadFromJsonAsync<SettingView<FeatlyRateLimitSettings>>(TestJson.Options, ct);
        after!.Source.Should().Be(nameof(FeatlySettingsSource.Database));

        // The PUT above consumed nothing from the admin budget going forward:
        // fresh window, three reads pass, the fourth throttles.
        for (var i = 0; i < 3; i++)
        {
            (await admin.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        (await admin.GetAsync(FlagsUri, ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Negative_permits_are_rejected()
    {
        using var host = await BuildHostAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = AdminClient(host);

        (await admin.PutAsJsonAsync("/api/admin/settings/rate-limit", new { enabled = true, adminPermitsPerMinute = -1 }, ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    private static async Task<IHost> BuildHostAsync(Dictionary<string, string?>? extraConfig = null)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) =>
                {
                    var data = new Dictionary<string, string?>
                    {
                        ["Featly:Server:AdminApiKey"] = AdminKey,
                        ["Featly:Server:SdkApiKey"] = SdkKey,
                    };
                    foreach (var kv in extraConfig ?? [])
                    {
                        data[kv.Key] = kv.Value;
                    }
                    config.AddInMemoryCollection(data);
                });
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
