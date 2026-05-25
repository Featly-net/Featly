using Microsoft.Extensions.DependencyInjection;

namespace Featly.Sdk;

/// <summary>
/// Fluent builder returned by <c>services.AddFeatly()</c>. Lets callers chain
/// <c>UseServer(...)</c>, <c>UseEnvironment(...)</c>, etc. before exiting back
/// to the service collection.
/// </summary>
public sealed class FeatlyClientBuilder
{
    /// <summary>Creates a new builder over the supplied service collection.</summary>
    public FeatlyClientBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The underlying DI container being configured.</summary>
    public IServiceCollection Services { get; }
}
