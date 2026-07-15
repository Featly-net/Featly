using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Events;
using Featly.Storage;
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
/// Covers the M10 event backbone: the fan-out publisher's failure isolation,
/// and the audit recorder + `/api/admin/audit` end-to-end (a mutation produces
/// an audit entry the query endpoint returns and filters).
/// </summary>
public class AuditAndEventsTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    private sealed class RecordingConsumer : IFeatlyEventConsumer
    {
        public int Count { get; private set; }

        public ValueTask HandleAsync(FeatlyDomainEvent domainEvent, CancellationToken ct)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingConsumer : IFeatlyEventConsumer
    {
        public ValueTask HandleAsync(FeatlyDomainEvent domainEvent, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Publisher_isolates_a_failing_consumer_from_the_rest()
    {
        // Resolve the real publisher through DI (the concrete type is internal),
        // with a throwing consumer registered between two recording ones.
        var ok1 = new RecordingConsumer();
        var ok2 = new RecordingConsumer();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFeatlyInMemoryStore();
        services.AddFeatlyServer();
        services.AddSingleton<IFeatlyEventConsumer>(ok1);
        services.AddSingleton<IFeatlyEventConsumer>(new ThrowingConsumer());
        services.AddSingleton<IFeatlyEventConsumer>(ok2);
        using var provider = services.BuildServiceProvider();

        var publisher = provider.GetRequiredService<IFeatlyEventPublisher>();
        await publisher.PublishAsync(
            new FeatlyDomainEvent { Type = FeatlyEventTypes.FlagUpdated, EntityType = "Flag" },
            TestContext.Current.CancellationToken);

        // The throwing consumer didn't stop the others.
        ok1.Count.Should().Be(1);
        ok2.Count.Should().Be(1);
    }

    [Fact]
    public async Task Creating_then_updating_a_flag_writes_audit_entries()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        await admin.PostAsJsonAsync("/api/admin/flags", new
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
        }, ct);

        await admin.PutAsJsonAsync("/api/admin/flags/demo", new
        {
            key = "demo",
            name = "Demo (edited)",
            type = "Boolean",
            enabled = false,
            defaultVariantKey = "off",
            variants = new[]
            {
                new { key = "on", name = "On", value = true },
                new { key = "off", name = "Off", value = false },
            },
        }, ct);

        var entries = await admin.GetFromJsonAsync<List<AuditEntry>>(
            "/api/admin/audit", TestJson.Options, ct);

        entries.Should().NotBeNull();
        entries!.Select(e => e.Action).Should().Contain([FeatlyEventTypes.FlagCreated, FeatlyEventTypes.FlagUpdated]);
        var created = entries.Single(e => e.Action == FeatlyEventTypes.FlagCreated);
        created.EntityType.Should().Be("Flag");
        created.EntityKey.Should().Be("demo");
        created.ActorIdentifier.Should().NotBeNullOrEmpty();
        // Create carries the new state under `after`.
        created.Data.Should().NotBeNull();
        created.Data!.Value.GetProperty("after").GetProperty("enabled").GetBoolean().Should().BeTrue();

        // Update carries both states so the dashboard can render a real diff.
        var updated = entries.Single(e => e.Action == FeatlyEventTypes.FlagUpdated);
        var data = updated.Data!.Value;
        data.GetProperty("before").GetProperty("enabled").GetBoolean().Should().BeTrue();
        data.GetProperty("before").GetProperty("name").GetString().Should().Be("Demo");
        data.GetProperty("after").GetProperty("enabled").GetBoolean().Should().BeFalse();
        data.GetProperty("after").GetProperty("name").GetString().Should().Be("Demo (edited)");
    }

    [Fact]
    public async Task Audit_verify_endpoint_reports_an_intact_chain()
    {
        // Issue #208: real audited mutations build a hash chain that
        // GET /api/admin/audit/verify reports as intact.
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            await admin.PostAsJsonAsync("/api/admin/flags", new
            {
                key = $"demo{i}",
                name = "Demo",
                type = "Boolean",
                enabled = true,
                defaultVariantKey = "off",
                variants = new[] { new { key = "off", name = "Off", value = false } },
            }, ct);
        }

        var verdict = await admin.GetFromJsonAsync<AuditChainVerification>(
            "/api/admin/audit/verify", TestJson.Options, ct);
        verdict.Should().NotBeNull();
        verdict!.IsIntact.Should().BeTrue();
        verdict.EntriesChecked.Should().BeGreaterThanOrEqualTo(3);
        verdict.BrokenAtSequence.Should().BeNull();
    }

    [Fact]
    public async Task Audit_query_filters_by_entity_type()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var admin = AdminClient(host);
        var ct = TestContext.Current.CancellationToken;

        await admin.PostAsJsonAsync("/api/admin/flags", new
        {
            key = "f1",
            name = "F1",
            type = "Boolean",
            enabled = true,
            defaultVariantKey = "off",
            variants = new[] { new { key = "off", name = "Off", value = false } },
        }, ct);
        await admin.PostAsJsonAsync("/api/admin/configs", new
        {
            key = "c1",
            name = "C1",
            type = "Int",
            defaultValue = 1,
        }, ct);

        var flags = await admin.GetFromJsonAsync<List<AuditEntry>>(
            "/api/admin/audit?entityType=Config", TestJson.Options, ct);
        flags.Should().ContainSingle().Which.EntityKey.Should().Be("c1");
    }

    [Fact]
    public async Task Audit_endpoint_rejects_sdk_scope_key()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        var sdk = host.GetTestClient();
        sdk.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);

        var response = await sdk.GetAsync(new Uri("/api/admin/audit", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static HttpClient AdminClient(IHost host)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        return client;
    }

}
