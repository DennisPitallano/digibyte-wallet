using System.Net.Http.Json;
using System.Net;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/033 US3 (T037): POST /v1/pay/sessions/{id}/options/dd, the buyer's switch to DD (contract C1).</summary>
public class DdOptionEndpointTests
{
    [Fact]
    public async Task A_USD_price_becomes_DD_at_one_to_one_once_per_session()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var session = await CreateChoice(client, key, storeId, fiat: 25m);
        var id = session.GetProperty("id").GetString()!;
        var expiresAt = (await Load(factory, id)).ExpiresAt;

        var first = await Json(await PickDd(client, id));
        var again = await Json(await PickDd(client, id));

        Assert.Equal(TestDdKeys.Address(0), Address(first));
        Assert.Equal(2500, first.GetProperty("amountCentsDd").GetInt64());
        Assert.Equal(1m, first.GetProperty("usdRate").GetDecimal());
        Assert.Contains("currency=USD", first.GetProperty("uri").GetString());
        Assert.Equal(Address(first), Address(again));
        Assert.Equal(1, await DdAddressCount(factory));
        await factory.WithDb(async db =>
        {
            var row = await db.DdAddresses.SingleAsync(a => a.Address == TestDdKeys.Address(0));
            Assert.Equal(DdAddress.KindOption, row.Kind);
            Assert.Equal(0, row.Index);
            Assert.Equal(id, row.SessionId);
            Assert.Equal(storeId, row.StoreId);
        });
        Assert.Equal(expiresAt, (await Load(factory, id)).ExpiresAt); // switching never extends the payment
    }

    [Fact]
    public async Task A_peso_price_is_converted_at_the_USD_rate_and_rounded_up_to_the_cent()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var session = await CreateChoice(client, key, storeId, fiat: 1250m, currency: "PHP");

        var option = await Json(await PickDd(client, session.GetProperty("id").GetString()!));

        // 1250 / 58.2 = 21.4776… dollars → 2148 cents (never less than the price).
        Assert.Equal(2148, option.GetProperty("amountCentsDd").GetInt64());
        Assert.Equal(58.2m, option.GetProperty("usdRate").GetDecimal());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, option.GetProperty("rateAt").ValueKind);
    }

    [Fact]
    public async Task Concurrent_switches_make_one_option()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var id = (await CreateChoice(client, key, storeId)).GetProperty("id").GetString()!;

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PickDd(client, id)));

        var addresses = new HashSet<string>();
        foreach (var r in responses) addresses.Add(Address(await Json(r)));
        Assert.Single(addresses);
        await factory.WithDb(async db => Assert.Equal(1, await db.SessionOptions.CountAsync(o => o.SessionId == id && o.Currency == "DD")));
    }

    [Fact]
    public async Task A_store_bound_POS_tablet_can_switch_its_own_sale_to_DD()
    {
        // Regression (found by the end-to-end run): the POS sends its tablet key, and store-bound keys are refused on
        // endpoints not marked AllowPosKey.
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var pairing = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/pos/pairings", key, new { storeId, deviceLabel = "Till" }));
        var code = (await Json(pairing)).GetProperty("code").GetString();
        var tabletKey = (await Json(await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code }))).GetProperty("apiKey").GetString()!;
        var sale = await Json(await Create(client, tabletKey, new { storeId, amount = 2500m, fiatCurrency = "USD", fiatAmount = 25m, buyerChoice = true }));
        Assert.True(sale.GetProperty("buyerChoice").GetBoolean());

        var response = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, $"/v1/pay/sessions/{sale.GetProperty("id").GetString()}/options/dd", tabletKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_session_is_404()
    {
        using var factory = Factory();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await PickDd(client, "ses_nope")).StatusCode);
    }

    [Fact]
    public async Task A_payment_without_buyer_choice_has_no_DD_option()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var plain = await Json(await Create(client, key, new { storeId, amount = 5m }));

        AssertConflict(await PickDd(client, plain.GetProperty("id").GetString()!), "dd_option_not_allowed", "no_choice");
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("not_pending")]
    [InlineData("expired")]
    public async Task A_payment_that_moved_on_refuses_the_switch(string reason)
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var id = (await CreateChoice(client, key, storeId)).GetProperty("id").GetString()!;
        await factory.WithDb(async db =>
        {
            var s = await db.Sessions.FirstAsync(x => x.Id == id);
            s.Simulated = true; // keep the background monitor off this row
            if (reason == "locked") s.LockedAt = DateTime.UtcNow;
            if (reason == "not_pending") s.Status = PaySessionStatus.Seen;
            if (reason == "expired") s.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        AssertConflict(await PickDd(client, id), "dd_option_not_allowed", reason);
    }

    [Fact]
    public async Task DD_turned_off_after_creation_refuses_the_switch()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var id = (await CreateChoice(client, key, storeId)).GetProperty("id").GetString()!;
        (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = false })))
            .EnsureSuccessStatusCode();

        AssertConflict(await PickDd(client, id), "dd_option_unavailable", "dd_disabled");
    }

    [Fact]
    public async Task No_rate_for_the_price_currency_refuses_the_switch()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var id = (await CreateChoice(client, key, storeId, fiat: 100m, currency: "EUR")).GetProperty("id").GetString()!;

        AssertConflict(await PickDd(client, id), "dd_option_unavailable", "no_rate");
    }

    [Fact]
    public async Task A_price_outside_the_DD_limits_refuses_the_switch()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var id = (await CreateChoice(client, key, storeId, fiat: 0.5m)).GetProperty("id").GetString()!;

        var response = await PickDd(client, id);

        AssertConflict(response, "dd_option_unavailable", "out_of_range");
    }

    [Fact]
    public async Task A_down_explorer_refuses_the_switch_and_makes_no_option()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var id = (await CreateChoice(client, key, storeId)).GetProperty("id").GetString()!;
        factory.DdExplorer.Down = true;

        var response = await PickDd(client, id);

        // specs/035 contract C1: the address needs no outside service, but DD the explorer can't confirm isn't offered.
        AssertConflict(response, "dd_option_unavailable", "dd_service_unavailable");
        await factory.WithDb(async db => Assert.False(await db.SessionOptions.AnyAsync(o => o.SessionId == id && o.Currency == "DD")));
    }

    [Fact]
    public async Task The_public_session_says_whether_DD_can_still_be_picked()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        var id = (await CreateChoice(client, key, storeId)).GetProperty("id").GetString()!;
        await PickDd(client, id);

        var open = await Json(await client.GetAsync($"/v1/pay/sessions/{id}"));
        Assert.True(open.GetProperty("ddAvailable").GetBoolean());
        Assert.Equal(2, open.GetProperty("session").GetProperty("options").GetArrayLength());

        (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = false })))
            .EnsureSuccessStatusCode();
        var off = await Json(await client.GetAsync($"/v1/pay/sessions/{id}"));
        Assert.False(off.GetProperty("ddAvailable").GetBoolean());
        Assert.Equal("dd_disabled", off.GetProperty("ddReason").GetString()); // even though a DD option exists
    }
}
