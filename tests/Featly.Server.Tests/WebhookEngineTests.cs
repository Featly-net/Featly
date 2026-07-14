using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Webhooks;
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
/// Covers the M10 webhook engine: the HMAC signature, dispatcher matching,
/// retry backoff, and the admin CRUD + end-to-end dispatch (a mutation enqueues
/// a delivery for a matching endpoint).
/// </summary>
public class WebhookEngineTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";
    private static readonly string[] FlagUpdatedOnly = [FeatlyEventTypes.FlagUpdated];

    // ---- Signature ----------------------------------------------------------

    [Fact]
    public void Signature_matches_a_manual_hmac_and_varies_by_secret()
    {
        const string payload = """{"type":"flag.updated"}""";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("s3cret"));
        var expected = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

        WebhookSignature.Compute("s3cret", payload).Should().Be(expected);
        WebhookSignature.Compute("other", payload).Should().NotBe(expected);
    }

    // ---- Dispatcher matching ------------------------------------------------

    [Fact]
    public void Matches_respects_enabled_type_and_environment_filters()
    {
        var env = Guid.NewGuid();
        var evt = new FeatlyDomainEvent { Type = FeatlyEventTypes.FlagUpdated, EntityType = "Flag", EnvironmentId = env };

        // Empty subscription + no env filter = catch-all.
        WebhookDispatcher.Matches(new WebhookEndpoint { Name = "a", Url = "u" }, evt).Should().BeTrue();
        // Disabled never matches.
        WebhookDispatcher.Matches(new WebhookEndpoint { Name = "a", Url = "u", Enabled = false }, evt).Should().BeFalse();
        // Type filter excludes other types.
        WebhookDispatcher.Matches(new WebhookEndpoint { Name = "a", Url = "u", EventTypes = [FeatlyEventTypes.ConfigUpdated] }, evt).Should().BeFalse();
        WebhookDispatcher.Matches(new WebhookEndpoint { Name = "a", Url = "u", EventTypes = [FeatlyEventTypes.FlagUpdated] }, evt).Should().BeTrue();
        // Env filter must match.
        WebhookDispatcher.Matches(new WebhookEndpoint { Name = "a", Url = "u", EnvironmentId = Guid.NewGuid() }, evt).Should().BeFalse();
        WebhookDispatcher.Matches(new WebhookEndpoint { Name = "a", Url = "u", EnvironmentId = env }, evt).Should().BeTrue();
    }

    // ---- Backoff ------------------------------------------------------------

    [Fact]
    public void Backoff_grows_exponentially_and_caps()
    {
        var baseDelay = TimeSpan.FromSeconds(10);
        var maxDelay = TimeSpan.FromMinutes(1);
        WebhookDeliveryWorker.Backoff(1, baseDelay, maxDelay).Should().Be(TimeSpan.FromSeconds(10));
        WebhookDeliveryWorker.Backoff(2, baseDelay, maxDelay).Should().Be(TimeSpan.FromSeconds(20));
        WebhookDeliveryWorker.Backoff(3, baseDelay, maxDelay).Should().Be(TimeSpan.FromSeconds(40));
        // 4th would be 80s but caps at 60s.
        WebhookDeliveryWorker.Backoff(4, baseDelay, maxDelay).Should().Be(TimeSpan.FromMinutes(1));
    }

    // ---- Admin CRUD + dispatch ---------------------------------------------

    [Fact]
    public async Task Create_then_get_list_and_delete_a_webhook()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var create = await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "Relay",
            url = "https://example.com/hook",
            eventTypes = FlagUpdatedOnly,
        }, ct);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, cancellationToken: ct);
        created!.Secret.Should().NotBeNullOrEmpty(); // auto-generated

        (await admin.GetFromJsonAsync<List<WebhookEndpoint>>("/api/admin/webhooks", TestJson.Options, ct))
            .Should().ContainSingle();

        var del = await admin.DeleteAsync(new Uri($"/api/admin/webhooks/{created.Id}", UriKind.Relative), ct);
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync(new Uri($"/api/admin/webhooks/{created.Id}", UriKind.Relative), ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Read_paths_never_echo_the_signing_secret()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var created = await (await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "Relay",
            url = "https://example.com/hook",
            secret = "whsec_super-secret-value",
        }, ct)).Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, cancellationToken: ct);

        // Creation returns the secret exactly once (operator configures the receiver).
        created!.Secret.Should().Be("whsec_super-secret-value");

        // List and get must expose metadata only — the raw secret never appears.
        var listJson = await admin.GetStringAsync(new Uri("/api/admin/webhooks", UriKind.Relative), ct);
        listJson.Should().NotContain("whsec_super-secret-value");
        listJson.Should().Contain("hasSecret");

        var getJson = await admin.GetStringAsync(new Uri($"/api/admin/webhooks/{created.Id}", UriKind.Relative), ct);
        getJson.Should().NotContain("whsec_super-secret-value");
        getJson.Should().Contain("\"hasSecret\":true");
    }

    [Fact]
    public async Task Test_endpoint_enqueues_a_delivery()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var created = await (await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "Relay",
            url = "https://example.com/hook",
        }, ct)).Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, cancellationToken: ct);

        var test = await admin.PostAsync(new Uri($"/api/admin/webhooks/{created!.Id}/test", UriKind.Relative), null, ct);
        test.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var deliveries = await admin.GetFromJsonAsync<List<WebhookDelivery>>(
            $"/api/admin/webhooks/{created.Id}/deliveries", TestJson.Options, ct);
        deliveries.Should().ContainSingle().Which.EventType.Should().Be("webhook.test");
    }

    [Fact]
    public async Task Resend_enqueues_a_new_delivery_cloned_from_an_existing_one()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var created = await (await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "Relay",
            url = "https://example.com/hook",
        }, ct)).Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, cancellationToken: ct);

        await admin.PostAsync(new Uri($"/api/admin/webhooks/{created!.Id}/test", UriKind.Relative), null, ct);
        var first = (await admin.GetFromJsonAsync<List<WebhookDelivery>>(
            $"/api/admin/webhooks/{created.Id}/deliveries", TestJson.Options, ct))!.Single();

        var resend = await admin.PostAsync(
            new Uri($"/api/admin/webhooks/{created.Id}/deliveries/{first.Id}/resend", UriKind.Relative), null, ct);
        resend.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var deliveries = await admin.GetFromJsonAsync<List<WebhookDelivery>>(
            $"/api/admin/webhooks/{created.Id}/deliveries", TestJson.Options, ct);
        deliveries.Should().HaveCount(2);
        deliveries!.Should().OnlyContain(d => d.EventType == first.EventType);
        deliveries.Select(d => d.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Resend_returns_404_for_a_delivery_that_is_not_on_the_webhook()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var created = await (await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "Relay",
            url = "https://example.com/hook",
        }, ct)).Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, cancellationToken: ct);

        var resend = await admin.PostAsync(
            new Uri($"/api/admin/webhooks/{created!.Id}/deliveries/{Guid.NewGuid()}/resend", UriKind.Relative), null, ct);
        resend.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Flag_update_enqueues_a_delivery_for_a_matching_endpoint_only()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        // One endpoint subscribed to flag.updated, one to config.updated only.
        var flagHook = await (await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "flags",
            url = "https://example.com/flags",
            eventTypes = FlagUpdatedOnly,
        }, ct)).Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, cancellationToken: ct);
        var configHook = await (await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "configs",
            url = "https://example.com/configs",
            eventTypes = new[] { FeatlyEventTypes.ConfigUpdated },
        }, ct)).Content.ReadFromJsonAsync<WebhookEndpoint>(TestJson.Options, cancellationToken: ct);

        // Create + update a flag (update fires flag.updated).
        await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "demo",
            name = "Demo",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct);
        await admin.PutAsJsonAsync("/api/admin/flags/demo", new
        {
            key = "demo",
            name = "Demo 2",
            type = "Boolean",
            enabled = false,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct);

        var flagDeliveries = await admin.GetFromJsonAsync<List<WebhookDelivery>>(
            $"/api/admin/webhooks/{flagHook!.Id}/deliveries", TestJson.Options, ct);
        flagDeliveries.Should().Contain(d => d.EventType == FeatlyEventTypes.FlagUpdated);

        var configDeliveries = await admin.GetFromJsonAsync<List<WebhookDelivery>>(
            $"/api/admin/webhooks/{configHook!.Id}/deliveries", TestJson.Options, ct);
        configDeliveries.Should().BeEmpty(); // config-only endpoint ignores flag events
    }

    [Fact]
    public async Task Webhooks_endpoint_rejects_sdk_scope_key()
    {
        using var host = await BuildHostAsync();
        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await sdk.GetAsync(new Uri("/api/admin/webhooks", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // ---- SSRF guard (issue #189) -------------------------------------------

    [Theory]
    [InlineData("127.0.0.1", true)]      // loopback
    [InlineData("10.1.2.3", true)]       // 10/8
    [InlineData("172.16.5.4", true)]     // 172.16/12
    [InlineData("192.168.0.10", true)]   // 192.168/16
    [InlineData("169.254.169.254", true)] // link-local / cloud metadata
    [InlineData("100.64.1.1", true)]     // CGNAT
    [InlineData("::1", true)]            // IPv6 loopback
    [InlineData("fd00::1", true)]        // IPv6 unique-local
    [InlineData("fe80::1", true)]        // IPv6 link-local
    [InlineData("8.8.8.8", false)]       // public
    [InlineData("93.184.216.34", false)] // public (example.com)
    public void Guard_classifies_internal_addresses_as_blocked(string ip, bool blocked)
    {
        WebhookTargetGuard.IsBlocked(System.Net.IPAddress.Parse(ip)).Should().Be(blocked);
    }

    [Theory]
    [InlineData("https://example.com/hook", true)]
    [InlineData("http://example.com/hook", true)]
    [InlineData("ftp://example.com/hook", false)]   // non-http scheme
    [InlineData("http://localhost/hook", false)]    // localhost hostname
    [InlineData("http://127.0.0.1/hook", false)]    // loopback literal
    [InlineData("http://169.254.169.254/latest/meta-data", false)] // metadata literal
    public void Guard_write_check_allows_public_http_targets_only(string url, bool allowed)
    {
        WebhookTargetGuard.IsAllowedAtWrite(new Uri(url)).Should().Be(allowed);
    }

    [Theory]
    [InlineData("http://10.0.0.1/hook", false)]      // private literal
    [InlineData("http://127.0.0.1/hook", false)]     // loopback literal
    [InlineData("http://169.254.169.254/", false)]   // metadata literal
    [InlineData("http://[::1]/hook", false)]         // IPv6 loopback literal
    [InlineData("ftp://8.8.8.8/hook", false)]        // non-http scheme
    [InlineData("https://8.8.8.8/hook", true)]       // public literal
    [InlineData("http://localhost/hook", false)]     // hostname -> DNS resolves to loopback
    public async Task Delivery_guard_classifies_literal_targets(string url, bool allowed)
    {
        var result = await WebhookTargetGuard.IsAllowedAtDeliveryAsync(
            new Uri(url), TestContext.Current.CancellationToken);
        result.Should().Be(allowed);
    }

    [Theory]
    [InlineData("http://10.0.0.1/hook", false, false)]   // blocked when guard on
    [InlineData("https://8.8.8.8/hook", false, true)]    // public allowed
    [InlineData("not-a-url", false, false)]              // unparseable -> blocked
    [InlineData("http://10.0.0.1/hook", true, true)]     // opted in -> allowed
    public async Task Worker_target_check_respects_the_guard_and_opt_in(string url, bool allowPrivate, bool allowed)
    {
        var opts = new WebhookOptions { AllowPrivateNetworkTargets = allowPrivate };
        var result = await WebhookDeliveryWorker.IsTargetAllowedAsync(
            url, opts, TestContext.Current.CancellationToken);
        result.Should().Be(allowed);
    }

    [Fact]
    public async Task Create_rejects_an_internal_target_by_default()
    {
        using var host = await BuildHostAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var response = await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "metadata",
            url = "http://169.254.169.254/latest/meta-data/iam/",
        }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_allows_an_internal_target_when_opted_in()
    {
        using var host = await BuildHostAsync(allowPrivateTargets: true);
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        var response = await admin.PostAsJsonAsync("/api/admin/webhooks", new
        {
            name = "internal",
            url = "http://10.0.0.5/hook",
        }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Worker_dead_letters_a_delivery_whose_target_became_internal()
    {
        // A short poll interval so the hosted worker drains promptly.
        using var host = await BuildHostAsync(pollInterval: "00:00:00.200");
        var store = host.Services.GetRequiredService<Featly.Storage.IFeatlyStore>();
        var ct = TestContext.Current.CancellationToken;

        // Insert an endpoint pointing at an internal address directly (bypassing
        // the create-time guard) to simulate a target that resolves internally by
        // delivery time, then enqueue a delivery for it.
        var now = DateTimeOffset.UtcNow;
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            Name = "internal",
            Url = "http://10.0.0.1/hook",
            Secret = "whsec_test",
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.Webhooks.UpsertAsync(endpoint, ct);

        var delivery = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookEndpointId = endpoint.Id,
            EventType = "flag.updated",
            Payload = "{}",
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await store.WebhookDeliveries.EnqueueAsync([delivery], ct);

        WebhookDelivery? processed = null;
        for (var i = 0; i < 50; i++)
        {
            processed = await store.WebhookDeliveries.GetByIdAsync(delivery.Id, ct);
            if (processed is not null && processed.Status != WebhookDeliveryStatus.Pending)
            {
                break;
            }
            await Task.Delay(100, ct);
        }

        Assert.NotNull(processed); // narrows nullability for the accesses below
        processed.Status.Should().Be(WebhookDeliveryStatus.Dead);
        processed.LastError.Should().Contain("blocked");
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    private static async Task<IHost> BuildHostAsync(bool allowPrivateTargets = false, string pollInterval = "00:10:00")
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Featly:Server:AdminApiKey"] = AdminKey,
                    ["Featly:Server:SdkApiKey"] = SdkKey,
                    // Slow the delivery worker right down so it never races the
                    // assertions (tests that exercise the worker pass a short one).
                    ["Featly:Webhooks:PollInterval"] = pollInterval,
                    ["Featly:Webhooks:AllowPrivateNetworkTargets"] = allowPrivateTargets ? "true" : "false",
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
}
