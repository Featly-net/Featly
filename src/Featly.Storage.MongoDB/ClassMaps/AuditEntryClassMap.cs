using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class AuditEntryClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<AuditEntry>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(a => a.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
