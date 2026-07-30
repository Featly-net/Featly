using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class RoleClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<Role>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(r => r.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
