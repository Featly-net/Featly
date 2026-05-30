using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Featly.Server.Telemetry;

/// <summary>
/// DI extensions that wire Featly's server-side OpenTelemetry export pipeline.
/// </summary>
public static class FeatlyServerTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenTelemetry SDK for Featly's server when
    /// <c>Featly:Telemetry:Enabled</c> is <c>true</c>. Adds ASP.NET Core and
    /// HttpClient instrumentation, subscribes to Featly's own meter and activity
    /// source, and exports everything over OTLP. When the section is absent or
    /// disabled this method is a no-op, so there is no per-request overhead.
    /// </summary>
    /// <param name="services">The DI container being configured.</param>
    /// <param name="configuration">
    /// Application configuration. The <c>Featly:Telemetry</c> section is read here
    /// (rather than from <see cref="Microsoft.Extensions.Options.IOptions{T}"/>)
    /// because the OpenTelemetry pipeline must be built imperatively at startup.
    /// </param>
    public static IServiceCollection AddFeatlyServerTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Expose the options for diagnostics even when disabled.
        services
            .AddOptions<FeatlyTelemetryOptions>()
            .BindConfiguration(FeatlyTelemetryOptions.SectionName);

        var options = configuration
            .GetSection(FeatlyTelemetryOptions.SectionName)
            .Get<FeatlyTelemetryOptions>() ?? new FeatlyTelemetryOptions();

        if (!options.Enabled)
        {
            // Off by default: no OpenTelemetry pipeline, no instrumentation
            // middleware, no exporter. The Featly Meter/ActivitySource still
            // exist (registered by AddFeatlyServer) but nothing listens to them.
            return services;
        }

        var builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName));

        if (options.Traces)
        {
            builder.WithTracing(tracing => tracing
                .AddSource(FeatlyTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(exporter => ConfigureOtlp(exporter, options)));
        }

        if (options.Metrics)
        {
            builder.WithMetrics(metrics => metrics
                .AddMeter(FeatlyTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(exporter => ConfigureOtlp(exporter, options)));
        }

        return services;
    }

    private static void ConfigureOtlp(OtlpExporterOptions exporter, FeatlyTelemetryOptions options)
    {
        exporter.Protocol = options.OtlpProtocol == FeatlyOtlpProtocol.HttpProtobuf
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            exporter.Endpoint = new Uri(options.OtlpEndpoint);
        }
    }
}
