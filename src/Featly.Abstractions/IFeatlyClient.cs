namespace Featly;

/// <summary>
/// Entry point for application code. Aggregates the three client surfaces:
/// flags, configs, and events.
/// </summary>
/// <remarks>
/// Placeholder shape for M1. Sub-client interfaces are introduced in M2 (flags),
/// M4 (configs), and M9 (events). The SDK implementation lives in Featly.Sdk.
/// </remarks>
public interface IFeatlyClient
{
    // M2/M3: IFlagClient Flags { get; }
    // M4:    IConfigClient Configs { get; }
    // M9:    IEventClient Events { get; }
}
