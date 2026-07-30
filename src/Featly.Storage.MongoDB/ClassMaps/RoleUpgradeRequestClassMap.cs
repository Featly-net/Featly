using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class RoleUpgradeRequestClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<RoleUpgradeRequest>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(r => r.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
