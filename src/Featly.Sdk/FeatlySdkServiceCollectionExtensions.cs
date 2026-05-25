using System.Net.Http.Headers;
using Featly.Sdk.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Featly.Sdk;

/// <summary>
/// DI registration helpers for the Featly SDK.
/// </summary>
public static class FeatlySdkServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Featly SDK services and returns a <see cref="FeatlyClientBuilder"/>
    /// that callers chain configuration onto.
    /// </summary>
    public static FeatlyClientBuilder AddFeatly(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddOptions<FeatlySdkOptions>()
            .BindConfiguration(FeatlySdkOptions.SectionName);

        services.TryAddSingleton<FeatlySnapshotCache>();
        services.TryAddSingleton<IFlagClient>(sp =>
            new FlagClient(sp.GetRequiredService<FeatlySnapshotCache>()));
        services.TryAddSingleton<IFeatlyClient>(sp =>
            new FeatlyClient(sp.GetRequiredService<IFlagClient>()));

        return new FeatlyClientBuilder(services);
    }

    /// <summary>
    /// Configures the SDK to talk to a Featly server at <paramref name="serverUrl"/>
    /// using the supplied <paramref name="apiKey"/>.
    /// </summary>
    public static FeatlyClientBuilder UseServer(
        this FeatlyClientBuilder builder,
        string serverUrl,
        string apiKey,
        Action<FeatlySdkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var uri = new Uri(serverUrl, UriKind.Absolute);

        builder.Services.Configure<FeatlySdkOptions>(opts =>
        {
            opts.ServerUrl = uri;
            opts.ApiKey = apiKey;
            configure?.Invoke(opts);
        });

        builder.Services.AddHttpClient<FeatlyHttpClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<FeatlySdkOptions>>().Value;
            if (opts.ServerUrl is null)
            {
                throw new InvalidOperationException(
                    "FeatlySdkOptions.ServerUrl was not set. Did you forget to call UseServer()?");
            }
            client.BaseAddress = opts.ServerUrl;
            client.Timeout = opts.RequestTimeout;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Featly.Sdk/0.0.1");
        });

        builder.Services.AddHostedService<FeatlyConfigSyncService>();
        return builder;
    }

    /// <summary>Overrides the environment the SDK targets.</summary>
    public static FeatlyClientBuilder UseEnvironment(this FeatlyClientBuilder builder, string environmentKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentKey);

        builder.Services.Configure<FeatlySdkOptions>(opts => opts.EnvironmentKey = environmentKey);
        return builder;
    }
}
