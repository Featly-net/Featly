using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class UserClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<User>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(u => u.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
