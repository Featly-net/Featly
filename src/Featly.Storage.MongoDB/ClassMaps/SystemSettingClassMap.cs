using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

/// <summary>
/// <see cref="SystemSetting"/> has no separate row id — <see cref="SystemSetting.Key"/>
/// is the primary key on every relational provider too (e.g.
/// <c>SystemSettingConfiguration.HasKey(s => s.Key)</c>), so it maps directly
/// to <c>_id</c> here.
/// </summary>
internal static class SystemSettingClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<SystemSetting>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(s => s.Key);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
