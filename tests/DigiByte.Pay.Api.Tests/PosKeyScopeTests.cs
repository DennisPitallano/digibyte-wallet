using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Tests;

/// <summary>A paired POS tablet key (Kind "pos") only works on the store it was paired to.</summary>
public class PosKeyScopeTests
{
    private sealed record Setup(PayFactory Factory, HttpClient Client, string MerchantKey, string StoreA, string StoreB, string TabletKey);

    private static async Task<Setup> PairTablet()
    {
        var factory = new PayFactory();
        var client = factory.CreateClient();
        var (merchantKey, storeA) = await factory.RegisterMerchant(client);

        // A second store with a receive address, so sessions can be made there too.
        var storeB = "sto_secondstore00001";
        await factory.WithDb(async db =>
        {
            var merchantId = (await db.Stores.FirstAsync(s => s.Id == storeA)).MerchantId;
            db.Stores.Add(new PayStore { Id = storeB, MerchantId = merchantId, Name = "Second", Network = "mainnet", ReceiveAddress = PayFactory.NewAddress() });
            await db.SaveChangesAsync();
        });

        var pairing = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/pos/pairings", merchantKey, new { storeId = storeA, deviceLabel = "Front counter" }));
        Assert.Equal(HttpStatusCode.OK, pairing.StatusCode);
        var code = (await Json(pairing)).GetProperty("code").GetString();
        var claim = await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code });
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        var tabletKey = (await Json(claim)).GetProperty("apiKey").GetString()!;
        return new Setup(factory, client, merchantKey, storeA, storeB, tabletKey);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<string> CreateSession(HttpClient client, string key, string? storeId = null)
    {
        var response = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", key, new { amount = 1.5m, storeId }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task A_pairing_says_which_key_prefix_the_tablet_will_get()
    {
        var factory = new PayFactory();
        using var _ = factory;
        var client = factory.CreateClient();
        var (merchantKey, storeId) = await factory.RegisterMerchant(client);

        var pairing = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/pos/pairings", merchantKey, new { storeId }));
        var body = await Json(pairing);
        var prefix = body.GetProperty("prefix").GetString();
        var claim = await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code = body.GetProperty("code").GetString() });

        Assert.Equal(prefix, (await Json(claim)).GetProperty("prefix").GetString());
    }

    [Fact]
    public async Task Pairing_creates_a_pos_key_bound_to_the_paired_store()
    {
        var s = await PairTablet();
        using var _ = s.Factory;
        await s.Factory.WithDb(async db =>
        {
            var key = await db.ApiKeys.SingleAsync(k => k.Label == "POS — Front counter");
            Assert.Equal(ApiKeyKind.Pos, key.Kind);
            Assert.Equal(s.StoreA, key.StoreId);
        });
    }

    [Fact]
    public async Task Tablet_can_create_list_and_void_its_own_stores_sessions()
    {
        var s = await PairTablet();
        using var _ = s.Factory;

        // No storeId → the tablet's store; Source still comes out as pos:Device.
        var create = await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", s.TabletKey, new { amount = 2m }));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await Json(create);
        Assert.Equal(s.StoreA, created.GetProperty("storeId").GetString());
        Assert.Equal("pos:Front counter", created.GetProperty("source").GetString());
        var id = created.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", s.TabletKey, new { amount = 1m, storeId = s.StoreA }))).StatusCode);

        // The list only ever shows the tablet's store, even when another store has sessions.
        await CreateSession(s.Client, s.MerchantKey, s.StoreB);
        var list = await Json(await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/sessions", s.TabletKey)));
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        Assert.All(list.GetProperty("sessions").EnumerateArray(), e => Assert.Equal(s.StoreA, e.GetProperty("storeId").GetString()));

        Assert.Equal(HttpStatusCode.OK, (await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{s.StoreA}", s.TabletKey))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Get, $"/v1/pay/pos/shift-report?storeId={s.StoreA}", s.TabletKey))).StatusCode);
        var me = await Json(await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/me", s.TabletKey)));
        Assert.Equal(s.StoreA, Assert.Single(me.GetProperty("stores").EnumerateArray()).GetProperty("id").GetString());

        var voided = await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Post, $"/v1/pay/sessions/{id}/void", s.TabletKey, new { note = "customer left" }));
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        Assert.Equal("voided", (await Json(voided)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Tablet_gets_403_on_another_stores_sessions_and_store()
    {
        var s = await PairTablet();
        using var _ = s.Factory;
        var otherSession = await CreateSession(s.Client, s.MerchantKey, s.StoreB);

        var requests = new[]
        {
            PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", s.TabletKey, new { amount = 1m, storeId = s.StoreB }),
            PayFactory.Authed(HttpMethod.Post, $"/v1/pay/sessions/{otherSession}/void", s.TabletKey, new { }),
            PayFactory.Authed(HttpMethod.Get, $"/v1/pay/sessions?storeId={s.StoreB}", s.TabletKey),
            PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{s.StoreB}", s.TabletKey),
            PayFactory.Authed(HttpMethod.Get, $"/v1/pay/pos/shift-report?storeId={s.StoreB}", s.TabletKey),
        };
        foreach (var request in requests)
        {
            var response = await s.Client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{request.Method} {request.RequestUri} → {(int)response.StatusCode}");
            Assert.Contains("store it was paired to", (await Json(response)).GetProperty("error").GetString());
        }

        // The other store's session is untouched.
        await s.Factory.WithDb(async db =>
            Assert.Equal(PaySessionStatus.Pending, (await db.Sessions.SingleAsync(x => x.Id == otherSession)).Status));
    }

    [Fact]
    public async Task Tablet_gets_403_on_refunds_and_on_managing_keys_stores_webhooks_and_audit()
    {
        var s = await PairTablet();
        using var _ = s.Factory;
        var own = await CreateSession(s.Client, s.TabletKey);

        var requests = new[]
        {
            PayFactory.Authed(HttpMethod.Post, $"/v1/pay/sessions/{own}/refund", s.TabletKey, new { txid = new string('a', 64) }),
            PayFactory.Authed(HttpMethod.Get, "/v1/pay/api-keys", s.TabletKey),
            PayFactory.Authed(HttpMethod.Post, "/v1/pay/api-keys", s.TabletKey, new { label = "sneaky" }),
            PayFactory.Authed(HttpMethod.Post, "/v1/pay/stores", s.TabletKey, new { name = "New" }),
            PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{s.StoreA}", s.TabletKey, new { name = "Renamed" }),
            PayFactory.Authed(HttpMethod.Delete, $"/v1/pay/stores/{s.StoreB}", s.TabletKey),
            PayFactory.Authed(HttpMethod.Post, $"/v1/pay/stores/{s.StoreA}/webhook/test", s.TabletKey),
            PayFactory.Authed(HttpMethod.Get, $"/v1/pay/stores/{s.StoreA}/analytics", s.TabletKey),
            PayFactory.Authed(HttpMethod.Get, "/v1/pay/me/audit", s.TabletKey),
            PayFactory.Authed(HttpMethod.Patch, "/v1/pay/me", s.TabletKey, new { displayName = "Hijacked" }),
            PayFactory.Authed(HttpMethod.Get, "/v1/pay/pos/devices", s.TabletKey),
            PayFactory.Authed(HttpMethod.Post, "/v1/pay/pos/pairings", s.TabletKey, new { storeId = s.StoreB }),
        };
        foreach (var request in requests)
        {
            var response = await s.Client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{request.Method} {request.RequestUri} → {(int)response.StatusCode}");
            Assert.False(string.IsNullOrWhiteSpace((await Json(response)).GetProperty("error").GetString()));
        }

        await s.Factory.WithDb(async db =>
        {
            Assert.Equal(PaySessionStatus.Pending, (await db.Sessions.SingleAsync(x => x.Id == own)).Status);
            Assert.False(await db.ApiKeys.AnyAsync(k => k.Label == "sneaky"));
            Assert.True(await db.Stores.AnyAsync(x => x.Id == s.StoreB));
        });
    }

    [Fact]
    public async Task A_bad_secret_on_a_tablet_prefix_is_401_not_403()
    {
        var s = await PairTablet();
        using var _ = s.Factory;
        var forged = s.TabletKey[..s.TabletKey.LastIndexOf('_')] + "_wrongsecret";
        var response = await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/api-keys", forged));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Legacy_pos_key_without_a_store_keeps_its_old_access_and_still_lists_as_a_tablet()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (merchantKey, storeId) = await factory.RegisterMerchant(client);
        var (prefix, secret, hash) = Auth.MerchantAuthenticator.GenerateApiKey();
        await factory.WithDb(async db =>
        {
            var merchantId = (await db.Stores.FirstAsync(x => x.Id == storeId)).MerchantId;
            db.ApiKeys.Add(new PayApiKey { Id = "key_legacytablet0001", MerchantId = merchantId, Prefix = prefix, Hash = hash, Label = "POS — Old till", Kind = ApiKeyKind.Pos });
            await db.SaveChangesAsync();
        });
        var legacyKey = $"{prefix}_{secret}";

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/api-keys", legacyKey))).StatusCode);
        var create = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", legacyKey, new { amount = 1m })));
        Assert.Equal("pos:Old till", create.GetProperty("source").GetString());

        var devices = await Json(await client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/pos/devices", merchantKey)));
        var device = Assert.Single(devices.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, device.GetProperty("storeId").ValueKind);
    }

    [Fact]
    public async Task Lists_go_by_kind_so_a_merchant_key_named_POS_stays_an_api_key()
    {
        var s = await PairTablet();
        using var _ = s.Factory;
        var created = await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/api-keys", s.MerchantKey, new { label = "POS something" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var keys = await Json(await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/api-keys", s.MerchantKey)));
        var keyLabels = keys.EnumerateArray().Select(k => k.GetProperty("label").GetString()).ToList();
        Assert.Contains("POS something", keyLabels);
        Assert.DoesNotContain("POS — Front counter", keyLabels);

        var devices = await Json(await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/pos/devices", s.MerchantKey)));
        var device = Assert.Single(devices.EnumerateArray());
        Assert.Equal("POS — Front counter", device.GetProperty("label").GetString());
        Assert.Equal(s.StoreA, device.GetProperty("storeId").GetString());

        // A merchant key named "POS …" doesn't stamp sessions as POS sales.
        var newKey = (await Json(created)).GetProperty("apiKey").GetString()!;
        var session = await Json(await s.Client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/sessions", newKey, new { amount = 1m })));
        Assert.Equal(JsonValueKind.Null, session.GetProperty("source").ValueKind);
    }

    [Fact]
    public async Task Backfill_marks_POS_labelled_keys_as_tablets_and_binds_single_store_merchants()
    {
        using var factory = new PayFactory();
        await factory.WithDb(async db =>
        {
            db.Merchants.AddRange(
                new PayMerchant { Id = "mer_onestore00000001", DisplayName = "One", ApiKeyPrefix = "legacy_unused_a", ApiKeyHash = "x" },
                new PayMerchant { Id = "mer_twostores0000001", DisplayName = "Two", ApiKeyPrefix = "legacy_unused_b", ApiKeyHash = "x" });
            db.Stores.AddRange(
                new PayStore { Id = "sto_one0000000000001", MerchantId = "mer_onestore00000001", Name = "Only", Network = "mainnet" },
                new PayStore { Id = "sto_two0000000000001", MerchantId = "mer_twostores0000001", Name = "A", Network = "mainnet" },
                new PayStore { Id = "sto_two0000000000002", MerchantId = "mer_twostores0000001", Name = "B", Network = "mainnet" });
            db.ApiKeys.AddRange(
                new PayApiKey { Id = "key_bf01", MerchantId = "mer_onestore00000001", Prefix = "dgp_bf01", Hash = "x", Label = "POS — Till" },
                new PayApiKey { Id = "key_bf02", MerchantId = "mer_onestore00000001", Prefix = "dgp_bf02", Hash = "x", Label = "SDK initial key" },
                new PayApiKey { Id = "key_bf03", MerchantId = "mer_twostores0000001", Prefix = "dgp_bf03", Hash = "x", Label = "POS device — 2026-01-01" },
                new PayApiKey { Id = "key_bf04", MerchantId = "mer_twostores0000001", Prefix = "dgp_bf04", Hash = "x", Label = "pos lowercase is not a tablet" },
                new PayApiKey { Id = "key_bf05", MerchantId = "mer_twostores0000001", Prefix = "dgp_bf05", Hash = "x", Label = null });
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync(ApiKeyKindBackfill.Sql);
            db.ChangeTracker.Clear();

            var keys = await db.ApiKeys.Where(k => k.Id.StartsWith("key_bf")).ToDictionaryAsync(k => k.Id);
            Assert.Equal((ApiKeyKind.Pos, "sto_one0000000000001"), (keys["key_bf01"].Kind, keys["key_bf01"].StoreId));
            Assert.Equal((ApiKeyKind.Merchant, (string?)null), (keys["key_bf02"].Kind, keys["key_bf02"].StoreId));
            Assert.Equal((ApiKeyKind.Pos, (string?)null), (keys["key_bf03"].Kind, keys["key_bf03"].StoreId)); // two stores: can't tell
            Assert.Equal(ApiKeyKind.Merchant, keys["key_bf04"].Kind);
            Assert.Equal(ApiKeyKind.Merchant, keys["key_bf05"].Kind);
        });
    }
}
