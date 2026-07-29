using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Featly.Storage.MongoDB.Serialization;

/// <summary>
/// Serializes a <see cref="JsonElement"/> as a native BSON value (document,
/// array, string, number, bool, or null) rather than raw JSON text — the
/// document-store-native equivalent of every relational provider's
/// raw-JSON-text column mapping. Registered once, process-wide, via
/// <c>Featly.Storage.MongoDB.ClassMaps.MongoClassMaps.RegisterAll</c>; every
/// entity's condition/variant value flows through this same serializer.
/// </summary>
internal sealed class JsonElementSerializer : SerializerBase<JsonElement>
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(context);
        WriteElement(context.Writer, value);
    }

    public override JsonElement Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var stream = new MemoryStream();
        using (var jsonWriter = new Utf8JsonWriter(stream))
        {
            WriteReaderValueAsJson(context.Reader, jsonWriter);
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        // Clone detaches from the JsonDocument lifetime so it survives the using block.
        return doc.RootElement.Clone();
    }

    private static void WriteElement(IBsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartDocument();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WriteName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndDocument();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteString(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                {
                    writer.WriteInt64(longValue);
                }
                else
                {
                    writer.WriteDouble(element.GetDouble());
                }

                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBoolean(element.GetBoolean());
                break;

            default:
                // Covers JsonValueKind.Null/Undefined as well as any future
                // JsonValueKind Featly's own callers don't currently produce.
                writer.WriteNull();
                break;
        }
    }

    private static void WriteReaderValueAsJson(IBsonReader reader, Utf8JsonWriter jsonWriter)
    {
        switch (reader.GetCurrentBsonType())
        {
            case BsonType.Document:
                reader.ReadStartDocument();
                jsonWriter.WriteStartObject();
                while (reader.ReadBsonType() != BsonType.EndOfDocument)
                {
                    jsonWriter.WritePropertyName(reader.ReadName());
                    WriteReaderValueAsJson(reader, jsonWriter);
                }

                reader.ReadEndDocument();
                jsonWriter.WriteEndObject();
                break;

            case BsonType.Array:
                reader.ReadStartArray();
                jsonWriter.WriteStartArray();
                while (reader.ReadBsonType() != BsonType.EndOfDocument)
                {
                    WriteReaderValueAsJson(reader, jsonWriter);
                }

                reader.ReadEndArray();
                jsonWriter.WriteEndArray();
                break;

            case BsonType.String:
                jsonWriter.WriteStringValue(reader.ReadString());
                break;

            case BsonType.Int32:
                jsonWriter.WriteNumberValue(reader.ReadInt32());
                break;

            case BsonType.Int64:
                jsonWriter.WriteNumberValue(reader.ReadInt64());
                break;

            case BsonType.Double:
                jsonWriter.WriteNumberValue(reader.ReadDouble());
                break;

            case BsonType.Boolean:
                jsonWriter.WriteBooleanValue(reader.ReadBoolean());
                break;

            case BsonType.Null:
                reader.ReadNull();
                jsonWriter.WriteNullValue();
                break;

            case var bsonType:
                throw new NotSupportedException($"Unsupported BSON type '{bsonType}' when reading a JsonElement.");
        }
    }
}
