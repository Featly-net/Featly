namespace Featly;

/// <summary>
/// Point-in-time view of an environment's configuration, returned by
/// <c>GET /api/sdk/config</c> and consumed by the SDK cache.
/// </summary>
/// <remarks>
/// M4 adds dynamic configs alongside flags and segments.
/// </remarks>
public sealed record ConfigSnapshot(
    Guid EnvironmentId,
    string EnvironmentKey,
    DateTimeOffset At,
    IReadOnlyList<Flag> Flags,
    IReadOnlyList<Segment> Segments,
    IReadOnlyList<Config> Configs);
