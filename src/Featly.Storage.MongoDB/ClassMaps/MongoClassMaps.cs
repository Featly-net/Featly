using System.Text.Json;
using Featly.Storage.MongoDB.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Featly.Storage.MongoDB.ClassMaps;

/// <summary>
/// Registers every entity's document shape, once per process
/// (<see cref="MongoFeatlyDatabase"/> guards the call site). The document-
/// model equivalent of each relational provider's set of
/// <c>IEntityTypeConfiguration&lt;T&gt;</c> classes (ADR-0034) — same
/// intentional per-provider duplication ADR-0026 already accepted, now in a
/// different shape.
/// </summary>
internal static class MongoClassMaps
{
    public static void RegisterAll()
    {
        RegisterCommonSerializers();

        ProjectClassMap.Register();
        EnvironmentClassMap.Register();
        FlagClassMap.Register();
        SegmentClassMap.Register();
        ConfigClassMap.Register();
        UserClassMap.Register();
        RoleClassMap.Register();
        RoleAssignmentClassMap.Register();
        UserGroupClassMap.Register();
        RoleUpgradeRequestClassMap.Register();
    }

    /// <summary>
    /// <see cref="Guid"/> and <see cref="DateTimeOffset"/> need an explicit
    /// representation — the driver throws on first use otherwise
    /// ("GuidRepresentation Unspecified"). <see cref="JsonElement"/> gets the
    /// native-BSON-value fallback every entity's condition/variant value uses
    /// instead of the relational providers' raw-JSON-text column.
    /// <see cref="FlagType"/>/<see cref="ConditionOperator"/> are persisted by
    /// name, not ordinal — <see cref="ConditionOperator"/>'s own doc comment
    /// warns that reordering members is a breaking change to the on-disk
    /// format, same rule every relational provider's <c>HasConversion&lt;string&gt;()</c>
    /// already follows.
    /// </summary>
    private static void RegisterCommonSerializers()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.RegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
        BsonSerializer.RegisterSerializer(new JsonElementSerializer());
        BsonSerializer.RegisterSerializer(new EnumSerializer<FlagType>(BsonType.String));
        BsonSerializer.RegisterSerializer(new EnumSerializer<ConditionOperator>(BsonType.String));
        BsonSerializer.RegisterSerializer(new EnumSerializer<ConfigType>(BsonType.String));

        // Permission is documented as ordinal-stable on the enum itself
        // ("new permissions land at the end"), but every relational
        // provider overrides that with a by-name JSON/text conversion
        // instead (PermissionListSerializer) — storing names, not ints,
        // means a future reordering doesn't silently reshuffle a saved
        // role's grants. Mirrored here for the same reason.
        BsonSerializer.RegisterSerializer(new EnumSerializer<Permission>(BsonType.String));
        BsonSerializer.RegisterSerializer(new EnumSerializer<AssigneeType>(BsonType.String));
        BsonSerializer.RegisterSerializer(new EnumSerializer<RoleUpgradeStatus>(BsonType.String));
    }
}
