using System.Text.Json;

namespace Featly.Sdk.Internal;

/// <summary>
/// A telemetry event queued by the SDK for batched upload. Mirrors the server's
/// <c>EventIngestRequest</c> wire shape (camelCase JSON) — the environment and
/// row id are assigned server-side on ingest, so they are intentionally absent.
/// </summary>
internal sealed record QueuedEvent(
    EventType Type,
    string SubjectKey,
    string? FlagKey = null,
    string? ConfigKey = null,
    string? CustomKey = null,
    string? VariantKey = null,
    Dictionary<string, JsonElement>? Properties = null,
    DateTimeOffset At = default);
