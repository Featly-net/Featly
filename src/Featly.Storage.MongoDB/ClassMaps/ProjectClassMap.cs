using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class ProjectClassMap
{
    public static void Register()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Project)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<Project>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(p => p.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
