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
}
