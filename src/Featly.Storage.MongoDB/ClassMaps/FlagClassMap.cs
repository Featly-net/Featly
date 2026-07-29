using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class FlagClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<Variant>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Condition>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Split>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<RuleOutcome>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Rule>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Prerequisite>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Flag>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(f => f.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
