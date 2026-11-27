using System.Globalization;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/033 US2 (T032): the sessions CSV gives each row its own currency and amounts in it.</summary>
public class SessionsCsvDdTests
{
    [Fact]
    public async Task A_DD_session_is_exported_in_dollars_with_its_currency_and_no_DGB()
    {
        using var factory = new PayFactory();
        factory.DdNode.Addresses.Enqueue("DDcsv000001");
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        await TestDdKeys.GiveKey(client, key, storeId);
        (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = true })))
            .EnsureSuccessStatusCode();
        (await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", key, new { storeId, currency = "DD", amountCentsDd = 1250 })))
            .EnsureSuccessStatusCode();
        (await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", key, new { storeId, amount = 2.5m })))
            .EnsureSuccessStatusCode();

        var csv = await (await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/sessions?format=csv", key))).Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var header = lines[0].Split(',');

        // New columns are appended, so existing positions don't move.
        Assert.Equal(new[] { "currency", "amount", "received" }, header[^3..]);
        Assert.Equal("amountDgb", header[4]);
        var dd = lines.Single(l => l.Contains("DDcsv000001")).Split(',');
        Assert.Equal("DD", dd[^3]);
        Assert.Equal(12.5m, Number(dd[^2]));
        Assert.Equal(0m, Number(dd[Array.IndexOf(header, "receivedDgb")]));
        var dgb = lines.Skip(1).Single(l => !l.Contains("DDcsv000001")).Split(',');
        Assert.Equal("DGB", dgb[^3]);
        Assert.Equal(2.5m, Number(dgb[^2]));
    }

    private static decimal Number(string field) =>
        decimal.Parse(field, NumberStyles.Number, CultureInfo.InvariantCulture);
}
