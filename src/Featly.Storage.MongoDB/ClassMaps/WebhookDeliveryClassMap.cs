using MongoDB.Bson.Serialization;

namespace Featly.Storage.MongoDB.ClassMaps;

internal static class WebhookDeliveryClassMap
{
    public static void Register()
    {
        BsonClassMap.RegisterClassMap<WebhookDelivery>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(d => d.Id);
            cm.SetIgnoreExtraElements(true);
        });
    }
}
