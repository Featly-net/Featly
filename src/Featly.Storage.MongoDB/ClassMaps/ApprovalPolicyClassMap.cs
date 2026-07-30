using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class ApprovalPolicyClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<ApproverRule>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<ApprovalPolicy>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(p => p.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
