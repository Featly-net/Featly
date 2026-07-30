using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class WebhookEndpointClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<WebhookEndpoint>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(e => e.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
