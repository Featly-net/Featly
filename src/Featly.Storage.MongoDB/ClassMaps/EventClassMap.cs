using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class EventClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<Event>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(e => e.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
