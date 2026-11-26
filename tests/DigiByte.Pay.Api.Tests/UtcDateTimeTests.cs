using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigiByte.Pay.Api.Tests;

// Times leave the API as UTC with a "Z". SQLite returns them without a kind, and a browser reads a zone-less time
// as local time, so a fresh checkout in a zone ahead of UTC used to look already expired.
public class UtcDateTimeTests
{
    [Fact]
    public void Unspecified_times_are_written_as_utc_with_a_z()
    {
        var options = new JsonSerializerOptions { Converters = { new UtcDateTimeConverter() } };
        var json = JsonSerializer.Serialize(new DateTime(2026, 11, 26, 2, 26, 2, DateTimeKind.Unspecified), options);
        Assert.Equal("\"2026-11-26T02:26:02Z\"", json);
        Assert.Equal(DateTimeKind.Utc, JsonSerializer.Deserialize<DateTime>("\"2026-11-26T02:26:02\"", options).Kind);
    }

    [Fact]
    public async Task A_session_from_the_api_has_zoned_timestamps()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var created = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", key, new { amount = 1.5m, storeId }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var body = await client.GetFromJsonAsync<JsonElement>($"/v1/pay/sessions/{id}");
        var session = body.GetProperty("session");
        Assert.EndsWith("Z", session.GetProperty("expiresAt").GetString());
        Assert.EndsWith("Z", session.GetProperty("createdAt").GetString());
    }
}
