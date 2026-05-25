using System.Text.Json.Serialization;
using Featly.Server.Authentication;
using Featly.Server.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Featly.Server;

/// <summary>DI extensions for registering Featly server-side services.</summary>
public static class FeatlyServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Featly server-side services. Storage must be added
    /// separately (for example, <c>services.AddFeatlyInMemoryStore()</c> or
    /// <c>services.AddFeatlySqliteStore(...)</c>).
    /// </summary>
    /// <param name="services">The DI container being configured.</param>
    /// <param name="configure">Optional in-line overrides for server options.</param>
    public static IServiceCollection AddFeatlyServer(
        this IServiceCollection services,
        Action<FeatlyServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind options from configuration (Featly:Server) and apply caller overrides.
        var optionsBuilder = services
            .AddOptions<FeatlyServerOptions>()
            .BindConfiguration(FeatlyServerOptions.SectionName);

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<IPostConfigureOptions<FeatlyApiKeyAuthenticationOptions>,
            FeatlyApiKeyOptionsBridge>();

        services
            .AddAuthentication()
            .AddScheme<FeatlyApiKeyAuthenticationOptions, FeatlyApiKeyAuthenticationHandler>(
                FeatlyAuthenticationDefaults.AdminScheme,
                opts => opts.Scope = "AdminWrite")
            .AddScheme<FeatlyApiKeyAuthenticationOptions, FeatlyApiKeyAuthenticationHandler>(
                FeatlyAuthenticationDefaults.SdkScheme,
                opts => opts.Scope = "SdkRead");

        services.AddAuthorizationBuilder()
            .AddPolicy(FeatlyAuthenticationDefaults.AdminPolicy, policy => policy
                .AddAuthenticationSchemes(FeatlyAuthenticationDefaults.AdminScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(FeatlyAuthenticationDefaults.SdkPolicy, policy => policy
                .AddAuthenticationSchemes(FeatlyAuthenticationDefaults.SdkScheme)
                .RequireAuthenticatedUser());

        services.AddHostedService<DefaultProjectBootstrapHostedService>();

        // Configure minimal API JSON to accept string enum values
        // (e.g. "Boolean" instead of 0) — the dashboard, curl examples,
        // and any human writing JSON expect named enums.
        services.Configure<JsonOptions>(json =>
        {
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true));
        });

        return services;
    }

    /// <summary>
    /// Bridges <see cref="FeatlyServerOptions"/> (where keys are configured) into
    /// the per-scheme <see cref="FeatlyApiKeyAuthenticationOptions"/> instances.
    /// </summary>
    private sealed class FeatlyApiKeyOptionsBridge(
        Microsoft.Extensions.Options.IOptionsMonitor<FeatlyServerOptions> server)
        : IPostConfigureOptions<FeatlyApiKeyAuthenticationOptions>
    {
        public void PostConfigure(string? name, FeatlyApiKeyAuthenticationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var serverOptions = server.CurrentValue;
            options.ApiKey = options.Scope switch
            {
                "AdminWrite" => serverOptions.AdminApiKey,
                "SdkRead" => serverOptions.SdkApiKey,
                _ => options.ApiKey,
            };
        }
    }
}
