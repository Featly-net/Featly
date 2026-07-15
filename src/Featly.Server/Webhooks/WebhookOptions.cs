namespace Featly.Server.Webhooks;

/// <summary>
/// Tuning for the webhook delivery worker. Bound from <c>Featly:Webhooks</c>;
/// the defaults suit a single-node deployment.
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Featly:Webhooks";

    /// <summary>How often the worker scans the queue for due deliveries.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum due deliveries claimed per scan.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Total attempts before a delivery is dead-lettered.</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Base delay for exponential backoff (delay = base * 2^(attempt-1), capped).</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound on a single retry delay.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Per-request timeout for an individual delivery POST.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Consecutive failures that trip the per-endpoint circuit breaker (issue
    /// #207). Once an endpoint reaches this many failures in a row its circuit
    /// opens and due deliveries are short-circuited for <see cref="CircuitBreakerCooldown"/>.
    /// A non-positive value disables the breaker (backoff + dead-letter still
    /// apply). DB-overridable via webhook settings.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// How long a tripped circuit stays open before the next half-open probe
    /// (issue #207). DB-overridable via webhook settings.
    /// </summary>
    public TimeSpan CircuitBreakerCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When <c>true</c>, disables the SSRF guard that blocks webhook targets on
    /// loopback / private / link-local ranges (issue #189). Off by default;
    /// enable only when you intentionally deliver to an internal receiver and
    /// understand the SSRF exposure. This is a network-policy switch, so it is
    /// bootstrap config (<c>Featly:Webhooks</c>), not a DB-overridable setting.
    /// </summary>
    public bool AllowPrivateNetworkTargets { get; set; }
}
