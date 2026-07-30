using MongoDB.Driver;

namespace Featly.Storage.MongoDB.Stores;

internal sealed class MongoEventStore(MongoFeatlyDatabase database) : IEventStore
{
    public async Task AppendAsync(IReadOnlyList<Event> events, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        await database.Events.InsertManyAsync(events, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Event>> QueryAsync(
        Guid environmentId,
        EventType? type = null,
        string? flagKey = null,
        string? customKey = null,
        CancellationToken ct = default)
    {
        var filter = Builders<Event>.Filter.Eq(e => e.EnvironmentId, environmentId);

        if (type is not null)
        {
            filter &= Builders<Event>.Filter.Eq(e => e.Type, type.Value);
        }

        if (flagKey is not null)
        {
            filter &= Builders<Event>.Filter.Eq(e => e.FlagKey, flagKey);
        }

        if (customKey is not null)
        {
            filter &= Builders<Event>.Filter.Eq(e => e.CustomKey, customKey);
        }

        return await database.Events
            .Find(filter)
            .SortBy(e => e.At)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
