using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class FlagClassMap
{
    public static void Register()
    {
        RegisterVariant();
        RegisterCondition();
        RegisterSplit();
        RegisterRuleOutcome();
        RegisterRule();
        RegisterPrerequisite();
        RegisterFlag();
    }

    private static void RegisterVariant()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Variant)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<Variant>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    private static void RegisterCondition()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Condition)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<Condition>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    private static void RegisterSplit()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Split)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<Split>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    private static void RegisterRuleOutcome()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(RuleOutcome)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<RuleOutcome>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    private static void RegisterRule()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Rule)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<Rule>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    private static void RegisterPrerequisite()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Prerequisite)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<Prerequisite>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
        });
    }

    private static void RegisterFlag()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Flag)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<Flag>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(f => f.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
