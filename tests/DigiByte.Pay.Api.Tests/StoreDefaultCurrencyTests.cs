using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Tests;

/// <summary>A store's default currency: "DGB" or a wallet fiat code, set on PATCH/POST /stores, read by every store DTO.</summary>
public class StoreDefaultCurrencyTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string? Currency(JsonElement store) =>
        store.GetProperty("defaultCurrency").ValueKind == JsonValueKind.Null ? null : store.GetProperty("defaultCurrency").GetString();

    private static Task<HttpResponseMessage> Patch(HttpClient client, string key, string storeId, object body) =>
        client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, body));

    [Fact]
    public async Task A_new_store_has_no_default_currency()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var store = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}", key)));
        Assert.Null(Currency(store));
    }

    [Theory]
    [InlineData("EUR", "EUR")]
    [InlineData("php", "PHP")]
    [InlineData(" dgb ", "DGB")]
    public async Task Patch_sets_and_normalises_the_default_currency(string sent, string stored)
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var response = await Patch(client, key, storeId, new { defaultCurrency = sent });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(stored, Currency(await Json(response)));

        Assert.Equal(stored, (await factory.LoadStore(storeId))!.DefaultCurrency);
        var list = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/stores", key)));
        Assert.Equal(stored, Currency(Assert.Single(list.EnumerateArray())));
        var me = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/me", key)));
        Assert.Equal(stored, Currency(Assert.Single(me.GetProperty("stores").EnumerateArray())));
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("DD")]
    [InlineData("US Dollar")]
    public async Task Patch_rejects_an_unknown_currency_with_400(string sent)
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        await Patch(client, key, storeId, new { defaultCurrency = "USD" });

        var response = await Patch(client, key, storeId, new { defaultCurrency = sent });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("defaultCurrency", (await Json(response)).GetProperty("error").GetString());
        Assert.Equal("USD", (await factory.LoadStore(storeId))!.DefaultCurrency);
    }

    [Fact]
    public async Task Empty_string_or_null_clears_it_and_a_missing_field_keeps_it()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        await Patch(client, key, storeId, new { defaultCurrency = "JPY" });
        Assert.Equal(HttpStatusCode.OK, (await Patch(client, key, storeId, new { name = "Renamed" })).StatusCode);
        Assert.Equal("JPY", (await factory.LoadStore(storeId))!.DefaultCurrency);

        Assert.Equal(HttpStatusCode.OK, (await Patch(client, key, storeId, new { defaultCurrency = "" })).StatusCode);
        Assert.Null((await factory.LoadStore(storeId))!.DefaultCurrency);

        await Patch(client, key, storeId, new { defaultCurrency = "DGB" });
        var cleared = await Patch(client, key, storeId, new { defaultCurrency = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null(Currency(await Json(cleared)));
        Assert.Null((await factory.LoadStore(storeId))!.DefaultCurrency);
    }

    [Fact]
    public async Task A_change_is_written_to_the_audit_log()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        await Patch(client, key, storeId, new { defaultCurrency = "GBP" });

        await factory.WithDb(async db =>
        {
            var events = await db.AuditEvents.Where(e => e.Action == "store.update" && e.TargetId == storeId).ToListAsync();
            Assert.Contains(events, e => e.Summary!.Contains("defaultCurrency"));
        });
    }

    [Fact]
    public async Task Post_stores_accepts_a_default_currency_and_rejects_a_bad_one()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, _) = await factory.RegisterMerchant(client);

        var created = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/stores", key, new { name = "Cafe", defaultCurrency = "cad" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("CAD", Currency(await Json(created)));

        var bad = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/stores", key, new { name = "Bad", defaultCurrency = "ABC" }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task A_paired_pos_tablet_sees_its_stores_default_currency()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        await Patch(client, key, storeId, new { defaultCurrency = "PHP" });

        var pairing = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/pos/pairings", key, new { storeId, deviceLabel = "Till" }));
        var code = (await Json(pairing)).GetProperty("code").GetString();
        var claim = await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code });
        var tabletKey = (await Json(claim)).GetProperty("apiKey").GetString()!;

        var store = await client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}", tabletKey));
        Assert.Equal(HttpStatusCode.OK, store.StatusCode);
        Assert.Equal("PHP", Currency(await Json(store)));
        var me = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/me", tabletKey)));
        Assert.Equal("PHP", Currency(Assert.Single(me.GetProperty("stores").EnumerateArray())));

        // The tablet can read the setting but not change it.
        var patch = await Patch(client, tabletKey, storeId, new { defaultCurrency = "USD" });
        Assert.NotEqual(HttpStatusCode.OK, patch.StatusCode);
    }
}
