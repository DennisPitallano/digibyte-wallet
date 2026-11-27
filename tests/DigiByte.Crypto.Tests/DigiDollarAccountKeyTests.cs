using System.Security.Cryptography;
using System.Text;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// specs/035 T004/T005: DigiDollar addresses derived from the merchant's account public key alone must equal the
/// addresses the wallet derives from the seed, and every wrong paste must be refused with a plain reason.
/// </summary>
public class DigiDollarAccountKeyTests
{
    private const string Words = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static DigiDollarKeyDerivation Seed(string network) => new(new Mnemonic(Words, Wordlist.English), network: network);

    private static Network Net(string network) => network == "mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet;

    private static string AccountKey(string network) => Seed(network).GetAccountExtPubKey().ToString(Net(network));

    [Theory]
    [InlineData("mainnet", 0)]
    [InlineData("mainnet", 1)]
    [InlineData("mainnet", 19)]
    [InlineData("testnet", 0)]
    [InlineData("testnet", 1)]
    [InlineData("testnet", 19)]
    public void Public_derivation_matches_seed_derivation(string network, int index)
    {
        var seed = Seed(network);
        var parsed = DigiDollarAccountKey.Parse(AccountKey(network), network);

        Assert.True(parsed.Ok, parsed.Reason);
        Assert.Equal(seed.GetAddress(seed.Derive(DigiDollarChain.Receive, index)), parsed.Key!.Address(index));
    }

    [Fact]
    public void Mainnet_and_testnet_addresses_carry_their_prefixes()
    {
        Assert.StartsWith("DD", DigiDollarAccountKey.Parse(AccountKey("mainnet"), "mainnet").Key!.Address(0));
        Assert.StartsWith("TD", DigiDollarAccountKey.Parse(AccountKey("testnet"), "testnet").Key!.Address(0));
    }

    [Fact]
    public void Account_key_exports_at_the_dd_account_path_and_round_trips()
    {
        var key = Seed("mainnet").GetAccountExtPubKey();
        Assert.Equal(3, key.Depth);
        Assert.StartsWith("xpub", key.ToString(DigiByteNetwork.Mainnet));
        Assert.StartsWith("tpub", Seed("testnet").GetAccountExtPubKey().ToString(DigiByteNetwork.Testnet));
    }

    [Fact]
    public void KeyId_is_sha256_of_network_and_canonical_key_and_stable()
    {
        var text = AccountKey("testnet");
        var a = DigiDollarAccountKey.Parse(text, "testnet").Key!;
        var b = DigiDollarAccountKey.Parse("  " + text + "\n", "testnet").Key!;

        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("testnet:" + text))).ToLowerInvariant();
        Assert.Equal(expected, a.KeyId);
        Assert.Equal(a.KeyId, b.KeyId);
        Assert.Equal(text, b.Canonical);
        Assert.Equal(expected[..8], a.Fingerprint);
        Assert.Equal("testnet", a.Network);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("DD1notakeyatall")]
    public void Garbage_is_malformed(string text)
    {
        var result = DigiDollarAccountKey.Parse(text, "mainnet");
        Assert.False(result.Ok);
        Assert.Equal(DigiDollarAccountKey.Malformed, result.Reason);
    }

    [Fact]
    public void A_bad_checksum_or_truncated_key_is_malformed()
    {
        var good = AccountKey("mainnet");
        var flipped = good[..^1] + (good[^1] == 'a' ? 'b' : 'a');

        Assert.Equal(DigiDollarAccountKey.Malformed, DigiDollarAccountKey.Parse(flipped, "mainnet").Reason);
        Assert.Equal(DigiDollarAccountKey.Malformed, DigiDollarAccountKey.Parse(good[..^10], "mainnet").Reason);
    }

    [Theory]
    [InlineData("mainnet")]
    [InlineData("testnet")]
    public void A_private_key_is_refused_without_echoing_it(string network)
    {
        var master = new Mnemonic(Words, Wordlist.English).DeriveExtKey();
        var xprv = master.Derive(new KeyPath("m/86'/20'/0'")).ToString(Net(network));

        var result = DigiDollarAccountKey.Parse(xprv, network);

        Assert.False(result.Ok);
        Assert.Equal(DigiDollarAccountKey.PrivateKey, result.Reason);
        Assert.Null(result.Key);
        Assert.DoesNotContain(xprv, result.ToString());
    }

    [Fact]
    public void A_private_key_for_the_other_network_is_still_a_private_key()
    {
        var master = new Mnemonic(Words, Wordlist.English).DeriveExtKey();
        Assert.Equal(DigiDollarAccountKey.PrivateKey, DigiDollarAccountKey.Parse(master.ToString(DigiByteNetwork.Testnet), "mainnet").Reason);
    }

    [Fact]
    public void A_key_for_the_other_network_is_refused()
    {
        Assert.Equal(DigiDollarAccountKey.WrongNetwork, DigiDollarAccountKey.Parse(AccountKey("testnet"), "mainnet").Reason);
        Assert.Equal(DigiDollarAccountKey.WrongNetwork, DigiDollarAccountKey.Parse(AccountKey("mainnet"), "testnet").Reason);
    }

    [Theory]
    [InlineData("m")]
    [InlineData("m/86'/20'/0'/0")]
    [InlineData("m/86'/20'/0'/0/0")]
    public void A_key_at_another_depth_is_not_an_account_key(string path)
    {
        var master = new Mnemonic(Words, Wordlist.English).DeriveExtKey();
        var key = (path == "m" ? master : master.Derive(new KeyPath(path))).Neuter().ToString(DigiByteNetwork.Mainnet);

        Assert.Equal(DigiDollarAccountKey.NotAccountKey, DigiDollarAccountKey.Parse(key, "mainnet").Reason);
    }

    [Fact]
    public void Unsupported_network_or_negative_index_throws()
    {
        Assert.Throws<ArgumentException>(() => DigiDollarAccountKey.Parse(AccountKey("mainnet"), "regtest"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DigiDollarAccountKey.Parse(AccountKey("mainnet"), "mainnet").Key!.Address(-1));
    }
}
