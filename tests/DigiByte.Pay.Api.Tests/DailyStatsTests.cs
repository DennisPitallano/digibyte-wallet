using System.Net;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Tests;

/// <summary>GET /v1/pay/stores/{id}/stats/daily: per-day sales on the merchant's own calendar days (the Overview charts).</summary>
public class DailyStatsTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string key, string url) =>
        client.SendAsync(PayFactory.Authed(HttpMethod.Get, url, key));

    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    /// <summary>Noon on a local day <paramref name="daysAgo"/> before today in <paramref name="zone"/>, as UTC: far from any day boundary.</summary>
    private static DateTime LocalNoon(TimeZoneInfo zone, int daysAgo)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var local = today.AddDays(-daysAgo).ToDateTime(new TimeOnly(12, 0));
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static int counter;

    /// <summary>Adds sessions straight to the database (so their creation time can be in the past).</summary>
    private static Task Seed(PayFactory factory, string storeId, params (DateTime CreatedAt, PaySessionStatus Status, long Sats)[] sessions) =>
        factory.WithDb(async db =>
        {
            var store = await db.Stores.FirstAsync(s => s.Id == storeId);
            foreach (var (createdAt, status, sats) in sessions)
            {
                var n = Interlocked.Increment(ref counter);
                db.Sessions.Add(new PaySession
                {
                    Id = $"ses_stats_{n:D6}_{Guid.NewGuid():N}"[..32],
                    MerchantId = store.MerchantId,
                    StoreId = storeId,
                    AddressIndex = 1000 + n,
                    Address = PayFactory.NewAddress(),
                    AmountSatoshis = sats,
                    Status = status,
                    CreatedAt = createdAt,
                    ExpiresAt = createdAt.AddMinutes(30),
                });
            }
            await db.SaveChangesAsync();
        });

    /// <summary>Moves the store's creation date back, so a previous period exists.</summary>
    private static Task Age(PayFactory factory, string storeId, int days) =>
        factory.WithDb(async db =>
        {
            var created = DateTime.UtcNow.AddDays(-days);
            await db.Stores.Where(s => s.Id == storeId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CreatedAt, created));
        });

    private static JsonElement DayOf(JsonElement body, DateOnly date) =>
        body.GetProperty("series").EnumerateArray().Single(d => d.GetProperty("date").GetString() == date.ToString("yyyy-MM-dd"));

    [Fact]
    public async Task A_new_store_gets_a_dense_series_of_zero_days_and_no_previous_period()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var response = await Get(client, key, $"/v1/pay/stores/{storeId}/stats/daily?days=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(7, body.GetProperty("days").GetInt32());
        Assert.False(body.GetProperty("storeHasPayments").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("previous").ValueKind);
        var series = body.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(7, series.Count);
        Assert.All(series, d => Assert.Equal(0, d.GetProperty("payments").GetInt32()));
        // Oldest first, consecutive days, ending today (UTC when no tz is given).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(today.ToString("yyyy-MM-dd"), series[^1].GetProperty("date").GetString());
        Assert.Equal(today.AddDays(-6).ToString("yyyy-MM-dd"), series[0].GetProperty("date").GetString());
        Assert.Equal(0m, body.GetProperty("totals").GetProperty("revenueDgb").GetDecimal());
    }

    [Fact]
    public async Task Counts_outcomes_and_sums_revenue_from_paid_and_confirmed_payments_only()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        var utc = TimeZoneInfo.Utc;
        await Seed(factory, storeId,
            (LocalNoon(utc, 1), PaySessionStatus.Paid, 150_000_000),
            (LocalNoon(utc, 1), PaySessionStatus.Confirmed, 50_000_000),
            (LocalNoon(utc, 1), PaySessionStatus.Expired, 900_000_000),
            (LocalNoon(utc, 2), PaySessionStatus.Underpaid, 100_000_000),
            (LocalNoon(utc, 2), PaySessionStatus.Refunded, 700_000_000),
            (LocalNoon(utc, 2), PaySessionStatus.Voided, 100_000_000),
            (LocalNoon(utc, 3), PaySessionStatus.Pending, 100_000_000),
            // Outside a 7-day window and the store is new, so there's no previous period to count it in.
            (LocalNoon(utc, 20), PaySessionStatus.Confirmed, 100_000_000));

        var body = await Json(await Get(client, key, $"/v1/pay/stores/{storeId}/stats/daily?days=7"));

        var totals = body.GetProperty("totals");
        Assert.Equal(7, totals.GetProperty("payments").GetInt32());
        Assert.Equal(2, totals.GetProperty("paid").GetInt32());
        Assert.Equal(1, totals.GetProperty("expired").GetInt32());
        Assert.Equal(1, totals.GetProperty("underpaid").GetInt32());
        Assert.Equal(1, totals.GetProperty("refunded").GetInt32());
        Assert.Equal(1, totals.GetProperty("voided").GetInt32());
        Assert.Equal(1, totals.GetProperty("open").GetInt32());
        Assert.Equal(2m, totals.GetProperty("revenueDgb").GetDecimal());
        Assert.True(body.GetProperty("storeHasPayments").GetBoolean());

        var yesterday = DayOf(body, DateOnly.FromDateTime(LocalNoon(utc, 1)));
        Assert.Equal(3, yesterday.GetProperty("payments").GetInt32());
        Assert.Equal(2, yesterday.GetProperty("paid").GetInt32());
        Assert.Equal(2m, yesterday.GetProperty("revenueDgb").GetDecimal());
    }

    [Fact]
    public async Task Days_are_the_merchants_own_calendar_days()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        var tokyo = Zone("Asia/Tokyo"); // UTC+9, no summer time
        // 00:30 in Tokyo yesterday is 15:30 UTC the day before: a different date in each zone.
        var tokyoDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tokyo)).AddDays(-1);
        var at = TimeZoneInfo.ConvertTimeToUtc(tokyoDay.ToDateTime(new TimeOnly(0, 30)), tokyo);
        await Seed(factory, storeId, (at, PaySessionStatus.Confirmed, 100_000_000));

        var inTokyo = await Json(await Get(client, key, $"/v1/pay/stores/{storeId}/stats/daily?days=7&tz=Asia/Tokyo"));
        var inUtc = await Json(await Get(client, key, $"/v1/pay/stores/{storeId}/stats/daily?days=7&tz=UTC"));

        Assert.Equal("Asia/Tokyo", inTokyo.GetProperty("timeZone").GetString());
        Assert.Equal(1, DayOf(inTokyo, tokyoDay).GetProperty("payments").GetInt32());
        Assert.Equal(1, DayOf(inUtc, DateOnly.FromDateTime(at)).GetProperty("payments").GetInt32());
        Assert.Equal(tokyoDay.AddDays(-1), DateOnly.FromDateTime(at));
        // The UTC range asked of the database starts at local midnight.
        var fromUtc = inTokyo.GetProperty("fromUtc").GetDateTime().ToUniversalTime();
        Assert.Equal(15, fromUtc.Hour);
    }

    [Fact]
    public async Task An_older_store_gets_the_previous_period_for_comparison()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        await Age(factory, storeId, 100);
        var utc = TimeZoneInfo.Utc;
        await Seed(factory, storeId,
            (LocalNoon(utc, 2), PaySessionStatus.Confirmed, 300_000_000),
            (LocalNoon(utc, 9), PaySessionStatus.Confirmed, 100_000_000),  // previous 7 days
            (LocalNoon(utc, 10), PaySessionStatus.Expired, 100_000_000),   // previous 7 days
            (LocalNoon(utc, 30), PaySessionStatus.Confirmed, 100_000_000)); // neither

        var body = await Json(await Get(client, key, $"/v1/pay/stores/{storeId}/stats/daily?days=7"));

        var previous = body.GetProperty("previous");
        Assert.Equal(JsonValueKind.Object, previous.ValueKind);
        Assert.Equal(2, previous.GetProperty("totals").GetProperty("payments").GetInt32());
        Assert.Equal(1m, previous.GetProperty("totals").GetProperty("revenueDgb").GetDecimal());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(today.AddDays(-13).ToString("yyyy-MM-dd"), previous.GetProperty("from").GetString());
        Assert.Equal(today.AddDays(-7).ToString("yyyy-MM-dd"), previous.GetProperty("to").GetString());
        Assert.Equal(3m, body.GetProperty("totals").GetProperty("revenueDgb").GetDecimal());
    }

    [Theory]
    [InlineData("days=0")]
    [InlineData("days=366")]
    [InlineData("tz=Not/AZone")]
    public async Task Bad_parameters_get_a_400(string query)
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var response = await Get(client, key, $"/v1/pay/stores/{storeId}/stats/daily?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_merchants_store_is_not_found_and_no_key_is_unauthorized()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (_, storeId) = await factory.RegisterMerchant(client);
        var (otherKey, _) = await factory.RegisterMerchant(client);

        var foreign = await Get(client, otherKey, $"/v1/pay/stores/{storeId}/stats/daily");
        var anonymous = await client.GetAsync($"/v1/pay/stores/{storeId}/stats/daily");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public void A_day_whose_midnight_is_skipped_starts_at_the_first_valid_time()
    {
        // Santiago moves clocks forward at midnight (00:00 -> 01:00) in early September.
        var santiago = Zone("America/Santiago");
        var day = Enumerable.Range(0, 60).Select(i => new DateOnly(2026, 8, 20).AddDays(i))
            .First(d => santiago.IsInvalidTime(d.ToDateTime(TimeOnly.MinValue)));

        var start = DailyStats.StartOfDayUtc(day, santiago);

        Assert.Equal(day, DailyStats.LocalDate(start, santiago));
        Assert.True(DailyStats.LocalDate(start.AddMinutes(-1), santiago) < day);
    }
}
