using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class RoleAssignmentClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<RoleAssignment>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(a => a.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
