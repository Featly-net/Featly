namespace Featly.Server.Approval;

/// <summary>
/// Tuning for <see cref="ScheduledApplyWorker"/> (ADR-0028). Bootstrap-only,
/// like <see cref="Webhooks.WebhookOptions.PollInterval"/> — this is worker
/// cadence, not a business policy an operator tunes via the dashboard.
/// </summary>
public sealed class ScheduledApplyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Featly:ScheduledApply";

    /// <summary>How often the worker scans for changes due to apply.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Maximum due changes claimed per scan.</summary>
    public int BatchSize { get; set; } = 50;
}
