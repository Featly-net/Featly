using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class UserGroupClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<UserGroup>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(g => g.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
