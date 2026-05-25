using Microsoft.Extensions.DependencyInjection;

namespace Featly.Server;

/// <summary>
/// DI extensions for registering Featly server-side services.
/// </summary>
/// <remarks>
/// Placeholder for M1. M2 wires the SDK config endpoint and EF migrations;
/// M6 adds auth pipeline; M8 adds the approval engine.
/// </remarks>
public static class FeatlyServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Featly server-side services. Storage must be added
    /// separately (for example, <c>services.AddFeatlyInMemoryStore()</c> or
    /// <c>services.AddFeatlySqliteStore(...)</c>).
    /// </summary>
    public static IServiceCollection AddFeatlyServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // M2+: register IFeatlySettingsProvider, evaluation engine, change notifier, ...
        return services;
    }
}
