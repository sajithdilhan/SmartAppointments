using System.Text.Json;
using System.Text.Json.Serialization;

namespace Booking.Application.Handlers;

/// <summary>
/// One serializer configuration for the idempotency record's body, so what is stored is read back
/// exactly (enums as strings, like the API's own responses).
/// </summary>
internal static class StoredResponseJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
