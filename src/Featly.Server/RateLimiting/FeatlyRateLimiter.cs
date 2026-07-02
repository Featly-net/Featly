using System.Threading.RateLimiting;
using Featly.Server.Settings;

namespace Featly.Server.RateLimiting;

/// <summary>Which Featly HTTP surface a request targets, for rate limiting.</summary>
public enum FeatlyRateSurface
{
    /// <summary>The auth endpoints (<c>/api/auth/*</c>) — login brute-force guard.</summary>
    Auth,

    /// <summary>The admin API (<c>/api/admin/*</c>).</summary>
    Admin,

    /// <summary>The SDK API (<c>/api/sdk/*</c>).</summary>
    Sdk,
}

/// <summary>
/// Process-wide request throttle over fixed one-minute windows, partitioned by
/// (surface, client, limit). The effective limits come from
/// <see cref="IFeatlySettingsProvider"/> on every acquisition, so a database
/// settings change takes effect without a restart: the changed limit lands in
/// the partition key, which starts a fresh window under the new limit while the
/// old partitions idle out.
/// </summary>
internal sealed class FeatlyRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<RateKey> _limiter;

    public FeatlyRateLimiter()
    {
        _limiter = PartitionedRateLimiter.Create<RateKey, RateKey>(static key =>
            RateLimitPartition.GetFixedWindowLimiter(key, static k => new FixedWindowRateLimiterOptions
            {
                PermitLimit = k.Limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    /// <summary>
    /// Attempts to take one permit for the client on the surface. A
    /// non-positive limit means the surface is unlimited.
    /// </summary>
    public ValueTask<RateLimitLease> AcquireAsync(FeatlyRateSurface surface, string client, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            return new ValueTask<RateLimitLease>(UnlimitedLease.Instance);
        }

        return _limiter.AcquireAsync(new RateKey(surface, client, limit), permitCount: 1, ct);
    }

    public void Dispose() => _limiter.Dispose();

    /// <summary>Partition key. Folding the limit in retires stale partitions on a settings change.</summary>
    internal readonly record struct RateKey(FeatlyRateSurface Surface, string Client, int Limit);

    private sealed class UnlimitedLease : RateLimitLease
    {
        public static readonly UnlimitedLease Instance = new();

        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
