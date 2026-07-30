using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class ApiKeyClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<ApiKey>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(k => k.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
