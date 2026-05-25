using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AbstractionsMarker = Featly.IFeatlyStore;
using StorageFacade = Featly.Storage.IFeatlyStore;

namespace Featly.Storage.InMemory;

/// <summary>DI extensions for the in-memory Featly store.</summary>
public static class InMemoryStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryFeatlyStore"/> as a singleton bound to both
    /// the storage facade (<see cref="StorageFacade"/>) and the lightweight
    /// marker (<see cref="AbstractionsMarker"/>) that non-storage layers depend on.
    /// </summary>
    public static IServiceCollection AddFeatlyInMemoryStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryFeatlyStore>();
        services.TryAddSingleton<StorageFacade>(sp => sp.GetRequiredService<InMemoryFeatlyStore>());
        services.TryAddSingleton<AbstractionsMarker>(sp => sp.GetRequiredService<InMemoryFeatlyStore>());

        return services;
    }
}
