using System.Net;
using System.Net.Http.Json;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/035 US1 (T019): the store's DigiDollar key, contracts/stores-api.md S1-S5.</summary>
public class DdKeyTests
{
    private static Task<HttpResponseMessage> Preview(HttpClient c, string apiKey, string storeId, string key) =>
        c.SendAsync(PayFactory.Authed(HttpMethod.Post, $"/v1/pay/stores/{storeId}/dd-key/preview", apiKey, new { key }));

    private static Task<HttpResponseMessage> Put(HttpClient c, string apiKey, string storeId, string key, string confirm) =>
        c.SendAsync(PayFactory.Authed(HttpMethod.Put, $"/v1/pay/stores/{storeId}/dd-key", apiKey, new { key, confirmFirstAddress = confirm }));

    private static Task<HttpResponseMessage> Remove(HttpClient c, string apiKey, string storeId) =>
        c.SendAsync(PayFactory.Authed(HttpMethod.Delete, $"/v1/pay/stores/{storeId}/dd-key", apiKey));

    private static async Task<(PayFactory F, HttpClient C, string Key, string StoreId)> Merchant()
    {
        var f = Factory();
        var c = f.CreateClient();
        var (key, storeId) = await f.RegisterMerchant(c); // a mainnet store
        return (f, c, key, storeId);
    }

    [Fact]
    public async Task Preview_shows_the_first_address_and_saves_nothing()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;

        var body = await Json(await Preview(c, key, storeId, TestDdKeys.Key()));

        Assert.Equal("mainnet", body.GetProperty("network").GetString());
        Assert.Equal(TestDdKeys.Address(0), body.GetProperty("firstAddress").GetString());
        Assert.Equal(TestDdKeys.KeyId()[..8], body.GetProperty("keyFingerprint").GetString());
        Assert.Null((await f.LoadStore(storeId))!.DdAccountKey);
    }

    [Fact]
    public async Task Each_wrong_paste_is_refused_with_its_reason()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;
        var master = new NBitcoin.Mnemonic(TestDdKeys.Words, NBitcoin.Wordlist.English).DeriveExtKey().Neuter()
            .ToString(DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet);

        foreach (var (paste, reason) in new[]
        {
            ("not a key", "malformed"),
            (TestDdKeys.Key("testnet"), "wrong_network"),
            (master, "not_account_key"),
        })
        {
            var response = await Preview(c, key, storeId, paste);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var json = await Json(response);
            Assert.Equal("dd_key_invalid", json.GetProperty("error").GetString());
            Assert.Equal(reason, json.GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task A_private_key_is_refused_and_never_echoed_or_stored()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;
        var xprv = TestDdKeys.PrivateKey();

        var preview = await Preview(c, key, storeId, xprv);
        var put = await Put(c, key, storeId, xprv, TestDdKeys.Address(0));

        foreach (var response in new[] { preview, put })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("private_key", text);
            Assert.DoesNotContain(xprv, text);
        }
        Assert.Null((await f.LoadStore(storeId))!.DdAccountKey);
        await f.WithDb(async db =>
            Assert.False(await db.AuditEvents.AnyAsync(a => a.Metadata != null && a.Metadata.Contains(xprv))));
    }

    [Fact]
    public async Task Saving_needs_the_first_address_the_merchant_checked()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;

        var wrong = await Put(c, key, storeId, TestDdKeys.Key(), TestDdKeys.Address(1));

        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Equal("first_address_mismatch", (await Json(wrong)).GetProperty("error").GetString());
        Assert.Null((await f.LoadStore(storeId))!.DdAccountKey);
    }

    [Fact]
    public async Task Saving_sets_the_key_creates_its_sequence_and_audits_only_the_fingerprint()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;

        var response = await Put(c, key, storeId, TestDdKeys.Key(), TestDdKeys.Address(0));

        response.EnsureSuccessStatusCode();
        var dto = (await Json(response)).GetProperty("ddKey");
        Assert.True(dto.GetProperty("set").GetBoolean());
        Assert.Equal(TestDdKeys.Address(0), dto.GetProperty("firstAddress").GetString());
        Assert.Equal(TestDdKeys.KeyId()[..8], dto.GetProperty("fingerprint").GetString());
        Assert.DoesNotContain(TestDdKeys.Key(), await response.Content.ReadAsStringAsync());

        var store = (await f.LoadStore(storeId))!;
        Assert.Equal(TestDdKeys.Key(), store.DdAccountKey);
        Assert.Equal(TestDdKeys.KeyId(), store.DdKeyId);
        await f.WithDb(async db =>
        {
            Assert.Equal(0, (await db.DdKeySequences.SingleAsync(k => k.Id == TestDdKeys.KeyId())).NextIndex);
            var audit = await db.AuditEvents.SingleAsync(a => a.Action == "store.dd_key_set");
            Assert.DoesNotContain(TestDdKeys.Key(), audit.Metadata ?? "");
            Assert.Contains(TestDdKeys.KeyId()[..8], audit.Metadata ?? "");
        });
    }

    [Fact]
    public async Task Replacing_the_key_is_audited_and_clears_the_donation_address()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;
        (await Put(c, key, storeId, TestDdKeys.Key(), TestDdKeys.Address(0))).EnsureSuccessStatusCode();
        await f.WithDb(async db =>
        {
            (await db.Stores.FirstAsync(s => s.Id == storeId)).DdDonationAddress = TestDdKeys.Address(0);
            await db.SaveChangesAsync();
        });

        var other = TestDdKeys.Key(words: TestDdKeys.OtherWords);
        (await Put(c, key, storeId, other, TestDdKeys.Address(0, words: TestDdKeys.OtherWords))).EnsureSuccessStatusCode();

        var store = (await f.LoadStore(storeId))!;
        Assert.Equal(other, store.DdAccountKey);
        Assert.Null(store.DdDonationAddress);
        await f.WithDb(async db => Assert.True(await db.AuditEvents.AnyAsync(a => a.Action == "store.dd_key_replaced")));
    }

    [Fact]
    public async Task DigiDollar_can_only_be_turned_on_with_a_key()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;

        var off = await c.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = true }));
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Equal("dd_key_required", (await Json(off)).GetProperty("error").GetString());

        (await Put(c, key, storeId, TestDdKeys.Key(), TestDdKeys.Address(0))).EnsureSuccessStatusCode();
        var on = await c.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = true }));
        on.EnsureSuccessStatusCode();
        Assert.True((await f.LoadStore(storeId))!.DigiDollarEnabled);
    }

    [Fact]
    public async Task Removing_the_key_turns_DigiDollar_off_and_is_audited()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;
        (await Put(c, key, storeId, TestDdKeys.Key(), TestDdKeys.Address(0))).EnsureSuccessStatusCode();
        (await c.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { digiDollarEnabled = true }))).EnsureSuccessStatusCode();

        var response = await Remove(c, key, storeId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var store = (await f.LoadStore(storeId))!;
        Assert.Null(store.DdAccountKey);
        Assert.Null(store.DdKeyId);
        Assert.False(store.DigiDollarEnabled);
        await f.WithDb(async db => Assert.True(await db.AuditEvents.AnyAsync(a => a.Action == "store.dd_key_removed")));
        var dto = await Json(await c.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}", key)));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, dto.GetProperty("ddKey").ValueKind);
    }

    [Fact]
    public async Task A_store_bound_POS_tablet_cant_set_the_key()
    {
        var (f, c, key, storeId) = await Merchant();
        using var _ = f;
        var pairing = await Json(await c.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/pos/pairings", key, new { storeId, deviceLabel = "Till" })));
        var tablet = (await Json(await c.PostAsJsonAsync("/v1/pay/pos/claim", new { code = pairing.GetProperty("code").GetString() })))
            .GetProperty("apiKey").GetString()!;

        Assert.Equal(HttpStatusCode.Forbidden, (await Preview(c, tablet, storeId, TestDdKeys.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(c, tablet, storeId, TestDdKeys.Key(), TestDdKeys.Address(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Remove(c, tablet, storeId)).StatusCode);
    }

    [Fact]
    public async Task The_unpaid_run_counts_addresses_since_the_last_paid_one_and_warns_past_500()
    {
        // specs/035 research R11 (T053).
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f);
        for (var i = 0; i < 3; i++)
            (await Create(c, key, new { storeId, currency = "DD", amountCentsDd = 500 })).EnsureSuccessStatusCode();

        async Task<System.Text.Json.JsonElement> DdKey() =>
            (await Json(await c.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{storeId}", key)))).GetProperty("ddKey");

        Assert.Equal(3, (await DdKey()).GetProperty("unpaidRun").GetInt32());

        // Index 1 gets paid: only index 2 is unpaid after it.
        await f.WithDb(async db =>
        {
            (await db.DdAddresses.SingleAsync(a => a.Index == 1)).FirstPaidAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
        Assert.Equal(1, (await DdKey()).GetProperty("unpaidRun").GetInt32());
        Assert.False((await DdKey()).GetProperty("unpaidRunWarning").GetBoolean());

        await f.WithDb(async db =>
        {
            (await db.DdKeySequences.SingleAsync()).NextIndex = 503;
            await db.SaveChangesAsync();
        });
        var warned = await DdKey();
        Assert.Equal(501, warned.GetProperty("unpaidRun").GetInt32());
        Assert.True(warned.GetProperty("unpaidRunWarning").GetBoolean());
        var me = await Json(await c.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/me", key)));
        Assert.True(me.GetProperty("stores")[0].GetProperty("ddKey").GetProperty("unpaidRunWarning").GetBoolean());
    }
}
