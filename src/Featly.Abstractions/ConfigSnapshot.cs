namespace Featly;

/// <summary>
/// Point-in-time view of an environment's configuration, returned by
/// <c>GET /api/sdk/config</c> and consumed by the SDK cache.
/// </summary>
/// <remarks>
/// M3 adds segments alongside flags so the SDK can resolve <c>InSegment</c>
/// conditions locally. Configs join the payload in M4.
/// </remarks>
public sealed record ConfigSnapshot(
    Guid EnvironmentId,
    string EnvironmentKey,
    DateTimeOffset At,
    IReadOnlyList<Flag> Flags,
    IReadOnlyList<Segment> Segments);
