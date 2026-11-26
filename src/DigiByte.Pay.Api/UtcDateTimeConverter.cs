using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigiByte.Pay.Api;

/// <summary>
/// Writes every DateTime as UTC with a "Z". The API stores UTC, but some databases (SQLite) hand values back without
/// a kind, which would otherwise be written with no zone; a browser then reads them as local time, so a fresh checkout
/// in a zone ahead of UTC looked already expired. Reading accepts any ISO 8601 value and normalises it to UTC.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc);
    }
}
