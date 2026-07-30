using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class PendingChangeClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<ChangeApproval>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<ChangeComment>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<PendingChange>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(c => c.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
