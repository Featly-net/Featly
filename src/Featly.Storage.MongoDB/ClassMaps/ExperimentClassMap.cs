using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class ExperimentClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<Experiment>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(e => e.Id);
            cm.UnmapMember(typeof(Experiment).GetProperty(nameof(Experiment.IsActive))!);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
