using System.Collections.Concurrent;

namespace Featly.Storage.InMemory;

internal sealed class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentQueue<Event> _events = new();

    public Task AppendAsync(IReadOnlyList<Event> events, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            _events.Enqueue(e);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Event>> QueryAsync(
        Guid environmentId,
        EventType? type = null,
        string? flagKey = null,
        string? customKey = null,
        CancellationToken ct = default)
    {
        var list = _events
            .Where(e => e.EnvironmentId == environmentId)
            .Where(e => type is null || e.Type == type)
            .Where(e => flagKey is null || string.Equals(e.FlagKey, flagKey, StringComparison.Ordinal))
            .Where(e => customKey is null || string.Equals(e.CustomKey, customKey, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult<IReadOnlyList<Event>>(list);
    }
}
