using System.Text.Json;

namespace Featly.Sdk.Internal;

/// <summary>
/// SDK implementation of <see cref="IEventClient"/>. Builds a custom
/// <see cref="QueuedEvent"/> from the call and the resolved subject, then hands
/// it to the non-blocking <see cref="IEventSink"/>. Never touches the network
/// inline — uploads happen on the background flush service.
/// </summary>
internal sealed class EventClient(IEventSink sink, IFeatlyContextAccessor contextAccessor) : IEventClient
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public ValueTask TrackAsync(
        string eventKey,
        object? properties = null,
        EvaluationContext? context = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        ct.ThrowIfCancellationRequested();

        var subject = (context ?? contextAccessor.Current)?.TargetingKey;
        if (string.IsNullOrEmpty(subject))
        {
            // No subject to attribute the event to — drop it rather than emit
            // an unattributable row the analytics can't pair with an exposure.
            return ValueTask.CompletedTask;
        }

        sink.Enqueue(new QueuedEvent(
            Type: EventType.Custom,
            SubjectKey: subject,
            CustomKey: eventKey,
            Properties: ToProperties(properties),
            At: DateTimeOffset.UtcNow));

        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, JsonElement>? ToProperties(object? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(properties, s_json);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.Clone();
        }

        return dict;
    }
}
