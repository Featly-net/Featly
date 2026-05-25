using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Featly.Storage.InMemory;

/// <summary>
/// DI extensions for wiring the in-memory Featly store.
/// </summary>
public static class InMemoryStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryFeatlyStore"/> as the singleton
    /// <see cref="IFeatlyStore"/>. Intended for tests, demos, and
    /// the embedded quickstart that does not need persistence.
    /// </summary>
    public static IServiceCollection AddFeatlyInMemoryStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFeatlyStore, InMemoryFeatlyStore>();
        return services;
    }
}
