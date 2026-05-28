namespace Featly;

/// <summary>
/// Entry point for application code. Aggregates the three client surfaces:
/// flags, configs, and events.
/// </summary>
/// <remarks>
/// M3 exposes flags. <c>IConfigClient</c> joins in M4 and <c>IEventClient</c>
/// joins in M9. The SDK implementation lives in <c>Featly.Sdk</c>.
/// </remarks>
public interface IFeatlyClient
{
    /// <summary>Client surface for evaluating feature flags.</summary>
    IFlagClient Flags { get; }

    /// <summary>Client surface for resolving dynamic configuration values.</summary>
    IConfigClient Configs { get; }

    /// <summary>Client surface for tracking custom telemetry events (M9+).</summary>
    IEventClient Events { get; }
}
