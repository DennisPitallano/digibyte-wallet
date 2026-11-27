using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// A store's DGB account key must be for the store's own network, and a private key is never accepted (specs/035 research R2
/// follow-up). Before this, the xpub check fell back to every network, so a mainnet xpub was taken on a testnet store.
/// </summary>
public class StoreXpubNetworkTests
{
    private static HdKeyDerivation Hd(Network network) => new(new Mnemonic(TestDdKeys.Words, Wordlist.English), network: network);
    private static string Xpub(Network network) => Hd(network).GetAccountExtPubKey().ToString(network);
    private static string Xprv(Network network) => Hd(network).DeriveAccount().ToString(network);

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Patch(HttpClient client, string key, string storeId, object body) =>
        client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, body));

    private static Task<HttpResponseMessage> Register(HttpClient client, string addressOrXpub, string network) =>
        client.PostAsJsonAsync("/v1/pay/merchants", new { displayName = "Test Shop", addressOrXpub, network });

    [Fact]
    public async Task Patch_accepts_an_xpub_for_the_stores_network()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var response = await Patch(client, key, storeId, new { addressOrXpub = Xpub(DigiByteNetwork.Mainnet) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Xpub(DigiByteNetwork.Mainnet), (await factory.LoadStore(storeId))!.Xpub);
    }

    [Fact]
    public async Task Patch_refuses_a_testnet_tpub_on_a_mainnet_store()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        var before = (await factory.LoadStore(storeId))!.ReceiveAddress;

        var response = await Patch(client, key, storeId, new { addressOrXpub = Xpub(DigiByteNetwork.Testnet) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("wrong_network", (await Json(response)).GetProperty("reason").GetString());
        var store = (await factory.LoadStore(storeId))!;
        Assert.Null(store.Xpub);
        Assert.Equal(before, store.ReceiveAddress);
    }

    [Fact]
    public async Task Patch_refuses_a_mainnet_xpub_on_a_testnet_store()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        // Network and key in one PATCH: the key is checked against the new network.
        var response = await Patch(client, key, storeId, new { network = "testnet", addressOrXpub = Xpub(DigiByteNetwork.Mainnet) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("wrong_network", body.GetProperty("reason").GetString());
        Assert.Contains("xpub", body.GetProperty("error").GetString());
        Assert.Null((await factory.LoadStore(storeId))!.Xpub);
    }

    [Fact]
    public async Task Patch_accepts_a_tpub_on_a_testnet_store()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var response = await Patch(client, key, storeId, new { network = "testnet", addressOrXpub = Xpub(DigiByteNetwork.Testnet) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Xpub(DigiByteNetwork.Testnet), (await factory.LoadStore(storeId))!.Xpub);
    }

    [Theory]
    [InlineData("mainnet")]
    [InlineData("testnet")]
    public async Task Patch_refuses_a_private_key_without_echoing_it(string keyNetwork)
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);
        var xprv = Xprv(keyNetwork == "mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet);

        var response = await Patch(client, key, storeId, new { addressOrXpub = xprv });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(xprv, raw);
        Assert.DoesNotContain(xprv[..20], raw);
        Assert.Equal("private_key", (await Json(response)).GetProperty("reason").GetString());
        Assert.Null((await factory.LoadStore(storeId))!.Xpub);
    }

    [Fact]
    public async Task Patch_keeps_the_old_error_text_for_garbage()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (key, storeId) = await factory.RegisterMerchant(client);

        var response = await Patch(client, key, storeId, new { addressOrXpub = "not-a-key" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Expected a DigiByte address or a BIP84 account-level xpub.", (await Json(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Registration_refuses_an_xpub_for_the_other_network()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();

        var response = await Register(client, Xpub(DigiByteNetwork.Mainnet), "testnet");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("wrong_network", (await Json(response)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Registration_refuses_a_private_key_without_echoing_it()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var xprv = Xprv(DigiByteNetwork.Mainnet);

        var response = await Register(client, xprv, "mainnet");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(xprv, await response.Content.ReadAsStringAsync());
        Assert.Equal("private_key", (await Json(response)).GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("mainnet")]
    [InlineData("testnet")]
    public async Task Registration_accepts_an_xpub_for_its_own_network(string network)
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var xpub = Xpub(network == "mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet);

        var response = await Register(client, xpub, network);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
