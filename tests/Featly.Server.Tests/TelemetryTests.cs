using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Featly.Server;
using Featly.Server.Telemetry;
using Featly.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// Covers the issue #77 server observability: the custom metrics fire on the
/// real code paths (recorded regardless of whether an exporter is attached), and
/// the OTLP export pipeline is wired only when <c>Featly:Telemetry:Enabled</c> is
/// set — off by default, so there is no instrumentation overhead.
/// </summary>
public class TelemetryTests
{
    private const string AdminKey = "admin-key-test";
    private const string SdkKey = "sdk-key-test";

    [Fact]
    public async Task Preview_evaluation_records_the_evaluations_counter()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var capture = MetricCapture.Attach(host);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var ct = TestContext.Current.CancellationToken;

        await client.PostAsJsonAsync("/api/admin/flags", new
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

        var response = await client.PostAsJsonAsync("/api/admin/preview/flags/demo", new { }, ct);
        response.EnsureSuccessStatusCode();

        capture.LongMeasurements.Should().Contain(m =>
            m.Name == "featly.server.evaluations"
            && m.Tags.Contains(new KeyValuePair<string, object?>("featly.entity_type", "flag")));
    }

    [Fact]
    public async Task Sdk_event_ingest_records_the_events_ingested_counter()
    {
        using var host = await FeatlyTestHost.CreateAsync();
        using var capture = MetricCapture.Attach(host);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SdkKey);
        var ct = TestContext.Current.CancellationToken;

        var response = await client.PostAsJsonAsync("/api/sdk/events", new
        {
            events = new object[]
            {
                new { type = "Exposure", subjectKey = "alice", flagKey = "demo", variantKey = "on" },
                new { type = "Custom", subjectKey = "alice", customKey = "checkout.completed" },
            },
        }, ct);
        response.EnsureSuccessStatusCode();

        capture.LongMeasurements.Should().Contain(m =>
            m.Name == "featly.server.events_ingested"
            && m.Tags.Contains(new KeyValuePair<string, object?>("featly.event_type", "Exposure")));
        capture.LongMeasurements.Should().Contain(m =>
            m.Name == "featly.server.events_ingested"
            && m.Tags.Contains(new KeyValuePair<string, object?>("featly.event_type", "Custom")));
    }

    [Fact]
    public void Telemetry_is_a_no_op_when_disabled()
    {
        var services = BuildTelemetryServices(enabled: false);

        services.Should().NotContain(d => d.ServiceType == typeof(TracerProvider));
        services.Should().NotContain(d => d.ServiceType == typeof(MeterProvider));
    }

    [Fact]
    public void Telemetry_registers_the_otel_pipeline_when_enabled()
    {
        var services = BuildTelemetryServices(enabled: true);

        services.Should().Contain(d => d.ServiceType == typeof(TracerProvider));
        services.Should().Contain(d => d.ServiceType == typeof(MeterProvider));
    }

    private static ServiceCollection BuildTelemetryServices(bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Featly:Telemetry:Enabled"] = enabled ? "true" : "false",
                ["Featly:Telemetry:OtlpEndpoint"] = "http://localhost:4317",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFeatlyServerTelemetry(configuration);
        return services;
    }

    /// <summary>
    /// Captures measurements from this host's <c>Featly.Server</c> meter only.
    /// Resolving the meter through the host's <see cref="IMeterFactory"/> returns
    /// the same cached instance the server records to, so a parallel test host's
    /// identically-named meter is filtered out by reference.
    /// </summary>
    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly List<Measurement> _long = [];
        private readonly Lock _gate = new();

        private MetricCapture(Meter meter)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ReferenceEquals(instrument.Meter, meter))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                lock (_gate)
                {
                    _long.Add(new Measurement(instrument.Name, value, tags.ToArray()));
                }
            });
            _listener.Start();
        }

        public IReadOnlyList<Measurement> LongMeasurements
        {
            get
            {
                lock (_gate)
                {
                    return [.. _long];
                }
            }
        }

        public static MetricCapture Attach(IHost host)
        {
            var factory = host.Services.GetRequiredService<IMeterFactory>();
            var meter = factory.Create(FeatlyTelemetry.MeterName);
            return new MetricCapture(meter);
        }

        public void Dispose() => _listener.Dispose();

        public sealed record Measurement(string Name, long Value, KeyValuePair<string, object?>[] Tags);
    }

}
