namespace Featly;

/// <summary>
/// Entry point for application code. Aggregates the three client surfaces:
/// flags, configs, and events.
/// </summary>
/// <remarks>
/// M2 exposes flags only. <c>IConfigClient</c> joins in M4 and <c>IEventClient</c>
/// joins in M9. The SDK implementation lives in <c>Featly.Sdk</c>.
/// </remarks>
public interface IFeatlyClient
{
    /// <summary>Client surface for evaluating feature flags.</summary>
    IFlagClient Flags { get; }
}
