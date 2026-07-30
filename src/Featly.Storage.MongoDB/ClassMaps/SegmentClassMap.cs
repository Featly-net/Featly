using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class SegmentClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<Segment>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(s => s.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
