using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigiByte.P2P.Shared.Sponsors;

/// <summary>JSON settings for the submission API so the wallet and the server agree on names: camelCase properties, snake_case enum values.</summary>
public static class SponsorJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return o;
    }
}
