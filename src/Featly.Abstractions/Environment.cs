namespace Featly;

/// <summary>
/// A scope inside a <see cref="Project"/> that isolates flags, configs,
/// segments, experiments, and API keys. Typical values: "production",
/// "staging", "development".
/// </summary>
/// <remarks>
/// Be careful: this type shadows <see cref="System.Environment"/> when both
/// namespaces are imported. Inside Featly code, prefer fully-qualifying as
/// <c>Featly.Environment</c> in callers that also use <c>System</c>.
/// </remarks>
public sealed class Environment
{
    /// <summary>Stable identifier for the row.</summary>
    public Guid Id { get; init; }

    /// <summary>Owning <see cref="Project"/>.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Unique short key inside the project. Used by SDK callers.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name shown in the dashboard.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// When <c>true</c>, this Environment is the default within its Project
    /// and is protected from accidental deletion. Auto-created on first boot.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// When <c>true</c>, the HTTP API rejects every mutation against this
    /// environment with <c>403 Environment ReadOnly</c>. Hard freeze for
    /// incidents and compliance windows.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>Audit: row creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
