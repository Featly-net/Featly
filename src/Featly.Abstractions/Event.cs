using System.Text.Json;

namespace Featly;

/// <summary>
/// A telemetry event ingested from an SDK client (ARCHITECTURE.md §16): either
/// an automatic <see cref="EventType.Exposure"/> emitted when a flag under an
/// active experiment is evaluated, or a <see cref="EventType.Custom"/> event the
/// application tracks (e.g. <c>checkout.completed</c>). The server pairs them by
/// <see cref="SubjectKey"/> to compute per-variant conversion rates.
/// </summary>
public sealed class Event
{
    /// <summary>Stable row id (assigned server-side on ingest).</summary>
    public Guid Id { get; init; }

    /// <summary>Whether this is an automatic exposure or an application-tracked custom event.</summary>
    public required EventType Type { get; init; }

    /// <summary>Flag key for an exposure event, or for a custom event raised in a flag's context.</summary>
    public string? FlagKey { get; init; }

    /// <summary>Config key, when the event relates to a dynamic config.</summary>
    public string? ConfigKey { get; init; }

    /// <summary>The custom event key (e.g. <c>checkout.completed</c>) for a <see cref="EventType.Custom"/> event.</summary>
    public string? CustomKey { get; init; }

    /// <summary>The subject the event is attributed to — pairs exposures with conversions.</summary>
    public required string SubjectKey { get; init; }

    /// <summary>The variant the subject saw, when known (set on exposures).</summary>
    public string? VariantKey { get; init; }

    /// <summary>Optional arbitrary properties (e.g. revenue, plan).</summary>
    public Dictionary<string, JsonElement>? Properties { get; init; }

    /// <summary>When the event occurred (client clock).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Environment the event belongs to.</summary>
    public required Guid EnvironmentId { get; init; }
}

/// <summary>The two kinds of <see cref="Event"/>.</summary>
public enum EventType
{
    /// <summary>Emitted automatically when a flag under an active experiment is evaluated.</summary>
    Exposure,

    /// <summary>Raised by the application via <c>IEventClient.TrackAsync</c>.</summary>
    Custom,
}
