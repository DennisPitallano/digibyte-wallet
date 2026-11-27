using System.Text.Json;
using DigiByte.Pay.Api.Data;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/033 US1 (T015): the POS shift report keeps DigiDollar apart from DGB, in USD cents.</summary>
public class PosShiftTotalsDdTests
{
    [Fact]
    public async Task DD_sessions_count_in_cents_and_never_in_DGB()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        var merchantId = (await factory.LoadStore(storeId))!.MerchantId;
        var n = 0;

        PaySession Session(string currency, PaySessionStatus status, long sats = 0, long? cents = null) => new()
        {
            Id = $"ses_shift{++n:D4}",
            MerchantId = merchantId,
            StoreId = storeId,
            AddressIndex = 0,
            Address = currency == "DD" ? $"DDshift{n}" : PayFactory.NewAddress(),
            AmountSatoshis = sats,
            AmountCentsDd = cents,
            Currency = currency,
            Status = status,
            ReceivedSatoshis = currency == "DD" ? cents ?? 0 : sats,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            Simulated = true, // keep the monitor away from these rows
        };

        await factory.WithDb(async db =>
        {
            db.Sessions.AddRange(
                Session("DGB", PaySessionStatus.Confirmed, sats: 250_000_000),
                Session("DGB", PaySessionStatus.Paid, sats: 50_000_000),
                Session("DD", PaySessionStatus.Confirmed, cents: 1250),
                Session("DD", PaySessionStatus.Confirmed, cents: 400),
                Session("DD", PaySessionStatus.Expired, cents: 900),
                Session("DGB", PaySessionStatus.Expired, sats: 100_000_000),
                Session("DD", PaySessionStatus.Underpaid, cents: 2000));
            await db.SaveChangesAsync();
        });

        var response = await client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/pos/shift-report?storeId={storeId}", key));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var totals = root.GetProperty("totals");
        Assert.Equal(4, totals.GetProperty("collectedCount").GetInt32());
        Assert.Equal(3m, totals.GetProperty("collectedDgb").GetDecimal());
        Assert.Equal(1650, totals.GetProperty("collectedDdCents").GetInt64());

        var expired = root.GetProperty("missed").GetProperty("expired");
        Assert.Equal(1m, expired.GetProperty("dgb").GetDecimal());
        Assert.Equal(900, expired.GetProperty("ddCents").GetInt64());

        var underpaid = root.GetProperty("missed").GetProperty("underpaid");
        Assert.Equal(0m, underpaid.GetProperty("dgb").GetDecimal());
        Assert.Equal(2000, underpaid.GetProperty("ddCents").GetInt64());
    }
}
