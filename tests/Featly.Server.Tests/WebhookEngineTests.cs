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

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Featly:Server:AdminApiKey"] = AdminKey,
                    ["Featly:Server:SdkApiKey"] = SdkKey,
                    // Slow the delivery worker right down so it never races the assertions.
                    ["Featly:Webhooks:PollInterval"] = "00:10:00",
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
