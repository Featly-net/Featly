using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class AssignmentClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<Assignment>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(a => a.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
