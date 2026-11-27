using System.Net;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// specs/033 Phase 2: the DD surfaces switch (FR-032), what <c>/v1/pay/me</c> and the store DTO report, and that
/// today's API-only DD keeps working with the switch off (SC-004) and after DD is turned off (FR-002).
/// </summary>
public class DdSurfacesGateTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static PayFactory WithGate(bool on) =>
        PayFactory.Create(settings: new Dictionary<string, string?> { [DdSurfacesGate.ConfigKey] = on ? "true" : "false" });

    // DigiDollar needs the store's own key first (specs/035 FR-004).
    private static async Task<HttpResponseMessage> SetDd(HttpClient client, string key, string storeId, bool on)
    {
        if (on && (await client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}", key))) is var r
            && (await r.Content.ReadAsStringAsync()).Contains("\"ddKey\":null"))
            await TestDdKeys.GiveKey(client, key, storeId);
        return await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = on }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Me_reports_the_switch(bool on)
    {
        using var factory = WithGate(on);
        var client = factory.CreateClient();
        var (key, _) = await factory.RegisterMerchant(client);

        var me = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/me", key)));

        Assert.Equal(on, me.GetProperty("ddSurfacesEnabled").GetBoolean());
        Assert.False(me.GetProperty("ddChoiceNoticePending").GetBoolean());
        var store = me.GetProperty("stores")[0];
        Assert.True(store.GetProperty("buyerChoiceEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, store.GetProperty("ddDonationAddress").ValueKind);
    }

    [Theory]
    [InlineData(false, true, "USD")]  // switch off: a saved DD default acts as USD
    [InlineData(true, false, "USD")]  // DD off for the store: same
    [InlineData(true, true, "DD")]    // both on: DD
    public async Task A_saved_DD_default_is_read_as_USD_unless_DD_can_be_offered(bool gate, bool ddOn, string effective)
    {
        using var factory = WithGate(gate);
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        await factory.WithDb(async db =>
        {
            var store = await db.Stores.FirstAsync(s => s.Id == storeId);
            store.DefaultCurrency = "DD";
            store.DigiDollarEnabled = ddOn;
            await db.SaveChangesAsync();
        });

        var store = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}", key)));

        Assert.Equal("DD", store.GetProperty("defaultCurrency").GetString());
        Assert.Equal(effective, store.GetProperty("defaultCurrencyEffective").GetString());
    }

    [Fact]
    public async Task With_the_switch_off_explicit_API_DD_sessions_still_work()
    {
        using var factory = WithGate(false);
        factory.DdNode.Addresses.Enqueue("DDexplicit0001");
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        (await SetDd(client, key, storeId, true)).EnsureSuccessStatusCode();

        var session = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", key,
            new { storeId, currency = "DD", amountCentsDd = 1250 })));

        Assert.Equal("DD", session.GetProperty("currency").GetString());
        Assert.Equal(1250, session.GetProperty("amountCentsDd").GetInt64());
        Assert.Equal("DDexplicit0001", session.GetProperty("address").GetString());
    }

    [Fact]
    public async Task A_DD_session_created_before_DD_is_turned_off_still_confirms()
    {
        using var factory = WithGate(false);
        factory.DdNode.Addresses.Enqueue("DDbeforeoff001");
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        (await SetDd(client, key, storeId, true)).EnsureSuccessStatusCode();
        var created = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", key,
            new { storeId, currency = "DD", amountCentsDd = 1250 })));
        var sessionId = created.GetProperty("id").GetString()!;

        (await SetDd(client, key, storeId, false)).EnsureSuccessStatusCode();
        factory.DdNode.Balances["DDbeforeoff001"] = (1250, 0);
        factory.DdNode.Txids["DDbeforeoff001"] = "ddtx0001";
        var monitor = factory.Services.GetServices<IHostedService>().OfType<InvoiceMonitor>().Single();
        await monitor.RunOnceAsync();

        PaySession? session = null;
        await factory.WithDb(async db => session = await db.Sessions.AsNoTracking().FirstAsync(s => s.Id == sessionId));
        Assert.Equal(PaySessionStatus.Confirmed, session!.Status);
        Assert.Equal(1250, session.ReceivedSatoshis);
    }

    [Fact]
    public async Task Availability_is_cached_and_reads_a_down_node_as_unavailable()
    {
        using var factory = WithGate(false);
        _ = factory.CreateClient();
        var availability = factory.Services.GetRequiredService<DdAvailability>();

        factory.DdNode.Healthy = false;
        Assert.False(await availability.IsAvailableAsync());

        factory.DdNode.Healthy = true;
        Assert.False(await availability.IsAvailableAsync()); // still the cached answer
    }
}
