namespace Featly.Server;

/// <summary>
/// Appsettings layer for audit-log retention, bound from <c>Featly:Audit</c>.
/// The DB-overridable effective value is exposed through the settings provider;
/// this is the <c>appsettings</c> tier of the precedence (ARCHITECTURE.md §15).
/// </summary>
public sealed class FeatlyAuditOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Featly:Audit";

    /// <summary>Days of audit history to keep; <c>0</c> (default) keeps everything.</summary>
    public int RetentionDays { get; set; }
}
