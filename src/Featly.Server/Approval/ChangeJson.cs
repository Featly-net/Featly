using System.Text.Json;
using System.Text.Json.Serialization;

namespace Featly.Server.Approval;

/// <summary>
/// Shared JSON options for the approval workflow: the write-request shapes a
/// change carries as its <see cref="PendingChange.ProposedState"/> are
/// serialized and deserialized with the same camelCase + string-enum settings
/// the minimal API uses, so a proposed state round-trips faithfully.
/// </summary>
internal static class ChangeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
    };
}
