using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Pay.Api.Tests;

/// <summary>Real DigiDollar account keys for tests (specs/035), from fixed test mnemonics.</summary>
internal static class TestDdKeys
{
    public const string Words = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    public const string OtherWords = "legal winner thank year wave sausage worth useful legal winner thank yellow";

    public static DigiDollarKeyDerivation Derivation(string network, string words = Words) =>
        new(new Mnemonic(words, Wordlist.English), network: network);

    /// <summary>The account key (xpub on mainnet, tpub on testnet) a merchant would paste.</summary>
    public static string Key(string network = "mainnet", string words = Words) =>
        Derivation(network, words).GetAccountExtPubKey().ToString(network == "mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet);

    /// <summary>The DD receive address at index <paramref name="i"/> for that key.</summary>
    public static string Address(int i, string network = "mainnet", string words = Words)
    {
        var d = Derivation(network, words);
        return d.GetAddress(d.Derive(DigiDollarChain.Receive, i));
    }

    public static string KeyId(string network = "mainnet", string words = Words) =>
        DigiDollarAccountKey.Parse(Key(network, words), network).Key!.KeyId;

    /// <summary>A private key at the DD account path, which must never be accepted.</summary>
    public static string PrivateKey(string network = "mainnet") =>
        new Mnemonic(Words, Wordlist.English).DeriveExtKey().Derive(new KeyPath("m/86'/20'/0'"))
            .ToString(network == "mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet);

    /// <summary>Give a (mainnet) store the test DigiDollar key through the API, so DigiDollar can be turned on.</summary>
    public static async Task GiveKey(HttpClient client, string apiKey, string storeId, string words = Words)
    {
        (await client.SendAsync(PayFactory.Authed(HttpMethod.Put, $"/v1/pay/stores/{storeId}/dd-key", apiKey,
            new { key = Key(words: words), confirmFirstAddress = Address(0, words: words) }))).EnsureSuccessStatusCode();
    }
}
