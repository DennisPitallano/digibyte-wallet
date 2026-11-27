using System.Net;
using DigiByte.Crypto.Networks;
using NBitcoin;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// Changing a store's network must not leave it holding a receive key or DigiDollar key for the old network (follow-up to
/// PR #132): the change is refused unless every saved key is valid on the new network or is replaced in the same request.
/// </summary>
public class StoreNetworkChangeTests
{
    private static string TestnetAddress() =>
        new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, DigiByteNetwork.Testnet).ToString();

    private static Task<HttpResponseMessage> Patch(HttpClient c, string key, string storeId, object body) =>
        c.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, body));

    private static async Task AssertStranded(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await Json(response);
        Assert.Equal("network_change_strands_keys", json.GetProperty("error").GetString());
        Assert.Equal(reason, json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_mainnet_receive_address_blocks_a_move_to_testnet()
    {
        using var f = Factory();
        var c = f.CreateClient();
        var (key, storeId) = await f.RegisterMerchant(c); // mainnet, with a mainnet address

        await AssertStranded(await Patch(c, key, storeId, new { network = "testnet" }), "receive");

        Assert.Equal("mainnet", (await f.LoadStore(storeId))!.Network);
    }

    [Fact]
    public async Task Replacing_the_receive_key_in_the_same_request_allows_the_move()
    {
        using var f = Factory();
        var c = f.CreateClient();
        var (key, storeId) = await f.RegisterMerchant(c);
        var testnetAddress = TestnetAddress();

        var response = await Patch(c, key, storeId, new { network = "testnet", addressOrXpub = testnetAddress });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var store = (await f.LoadStore(storeId))!;
        Assert.Equal("testnet", store.Network);
        Assert.Equal(testnetAddress, store.ReceiveAddress);
    }

    [Fact]
    public async Task A_mainnet_DigiDollar_key_blocks_a_move_to_testnet()
    {
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f, ddOn: false); // mainnet DigiDollar key
        var before = (await f.LoadStore(storeId))!.ReceiveAddress;

        await AssertStranded(await Patch(c, key, storeId, new { network = "testnet", addressOrXpub = TestnetAddress() }), "dd_key");

        var store = (await f.LoadStore(storeId))!;
        Assert.Equal("mainnet", store.Network);
        Assert.Equal(before, store.ReceiveAddress); // nothing from the refused request was saved
    }

    [Fact]
    public async Task Removing_the_DigiDollar_key_first_allows_the_move()
    {
        using var f = Factory();
        var (c, key, storeId) = await DdStore(f, ddOn: false);
        (await c.SendAsync(PayFactory.Authed(HttpMethod.Delete, $"/v1/pay/stores/{storeId}/dd-key", key))).EnsureSuccessStatusCode();

        var response = await Patch(c, key, storeId, new { network = "testnet", addressOrXpub = TestnetAddress() });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("testnet", (await f.LoadStore(storeId))!.Network);
    }

    [Fact]
    public async Task A_store_with_no_keys_moves_freely_and_the_same_network_is_a_no_op()
    {
        using var f = Factory();
        var c = f.CreateClient();
        var (key, storeId) = await f.RegisterMerchant(c);
        var created = await Json(await c.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/stores", key, new { name = "Empty", network = "mainnet" })));
        var emptyId = created.GetProperty("id").GetString()!;

        Assert.True((await Patch(c, key, emptyId, new { network = "testnet" })).IsSuccessStatusCode);
        Assert.True((await Patch(c, key, storeId, new { network = "mainnet" })).IsSuccessStatusCode); // unchanged network, keys kept
    }

    [Fact]
    public async Task An_address_for_the_other_network_is_refused()
    {
        using var f = Factory();
        var c = f.CreateClient();
        var (key, storeId) = await f.RegisterMerchant(c); // mainnet
        var before = (await f.LoadStore(storeId))!.ReceiveAddress;

        var response = await Patch(c, key, storeId, new { addressOrXpub = TestnetAddress() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("wrong_network", (await Json(response)).GetProperty("reason").GetString());
        Assert.Equal(before, (await f.LoadStore(storeId))!.ReceiveAddress);
    }
}
