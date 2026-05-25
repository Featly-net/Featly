namespace Featly;

/// <summary>
/// Point-in-time view of an environment's configuration, returned by
/// <c>GET /api/sdk/config</c> and consumed by the SDK cache.
/// </summary>
/// <remarks>
/// M2 carries flags only. Configs and segments join the payload as the
/// corresponding milestones come online (M3 segments, M4 configs).
/// </remarks>
public sealed record ConfigSnapshot(
    Guid EnvironmentId,
    string EnvironmentKey,
    DateTimeOffset At,
    IReadOnlyList<Flag> Flags);
