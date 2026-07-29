using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class EnvironmentClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<Environment>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(e => e.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
