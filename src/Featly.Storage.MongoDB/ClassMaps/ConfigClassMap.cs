using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class ConfigClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<ConfigRule>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Config>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
