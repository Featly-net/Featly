namespace Featly.Sdk.Internal;

/// <summary>
/// Concrete <see cref="IFeatlyClient"/>. Just routes property access to the
/// sub-clients held by the DI container.
/// </summary>
internal sealed class FeatlyClient(IFlagClient flags) : IFeatlyClient
{
    public IFlagClient Flags { get; } = flags;
}
