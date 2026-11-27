using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// <see cref="HdKeyDerivation.ParseXpubStrict"/>: the DigiPay store check for a DGB account key. Unlike
/// <see cref="HdKeyDerivation.ParseXpub"/> (used by the wallet's watch-only import), it never falls back to another network
/// and refuses private keys (specs/035 research R2 follow-up).
/// </summary>
public class HdKeyDerivationStrictXpubTests
{
    private const string Words = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static HdKeyDerivation Hd(Network network) => new(new Mnemonic(Words, Wordlist.English), network: network);

    private static string Xpub(Network network) => Hd(network).GetAccountExtPubKey().ToString(network);

    private static string Xprv(Network network) => Hd(network).DeriveAccount().ToString(network);

    private static ExtPubKey? Parse(string text, Network network, out string? refusal) =>
        HdKeyDerivation.ParseXpubStrict(text, network, out refusal);

    [Fact]
    public void Mainnet_xpub_on_mainnet_is_accepted()
    {
        var key = Parse(Xpub(DigiByteNetwork.Mainnet), DigiByteNetwork.Mainnet, out var refusal);
        Assert.NotNull(key);
        Assert.Null(refusal);
        Assert.Equal(Hd(DigiByteNetwork.Mainnet).GetAccountExtPubKey(), key);
    }

    [Fact]
    public void Testnet_tpub_on_testnet_is_accepted()
    {
        Assert.NotNull(Parse(Xpub(DigiByteNetwork.Testnet), DigiByteNetwork.Testnet, out var refusal));
        Assert.Null(refusal);
    }

    [Fact]
    public void Tpub_on_regtest_is_accepted_because_regtest_shares_testnets_version_bytes()
    {
        Assert.NotNull(Parse(Xpub(DigiByteNetwork.Testnet), DigiByteNetwork.Regtest, out var refusal));
        Assert.Null(refusal);
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.NotNull(Parse("  " + Xpub(DigiByteNetwork.Mainnet) + "\n", DigiByteNetwork.Mainnet, out _));
    }

    [Fact]
    public void Mainnet_xpub_on_testnet_is_refused_as_wrong_network()
    {
        Assert.Null(Parse(Xpub(DigiByteNetwork.Mainnet), DigiByteNetwork.Testnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.WrongNetwork, refusal);
    }

    [Fact]
    public void Testnet_tpub_on_mainnet_is_refused_as_wrong_network()
    {
        Assert.Null(Parse(Xpub(DigiByteNetwork.Testnet), DigiByteNetwork.Mainnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.WrongNetwork, refusal);
    }

    [Fact]
    public void Mainnet_xpub_on_regtest_is_refused_as_wrong_network()
    {
        Assert.Null(Parse(Xpub(DigiByteNetwork.Mainnet), DigiByteNetwork.Regtest, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.WrongNetwork, refusal);
    }

    [Theory]
    [InlineData("mainnet", "mainnet")]
    [InlineData("mainnet", "testnet")]
    [InlineData("testnet", "testnet")]
    [InlineData("testnet", "mainnet")]
    public void Private_keys_are_refused_for_either_network(string keyNetwork, string storeNetwork)
    {
        Assert.Null(Parse(Xprv(Net(keyNetwork)), Net(storeNetwork), out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.PrivateKey, refusal);
    }

    [Fact]
    public void A_bad_checksum_is_malformed()
    {
        var xpub = Xpub(DigiByteNetwork.Mainnet);
        var last = xpub[^1];
        var tampered = xpub[..^1] + (last == 'a' ? 'b' : 'a');

        Assert.Null(Parse(tampered, DigiByteNetwork.Mainnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.Malformed, refusal);
    }

    [Fact]
    public void A_truncated_key_is_malformed()
    {
        var xpub = Xpub(DigiByteNetwork.Mainnet);

        Assert.Null(Parse(xpub[..^6], DigiByteNetwork.Mainnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.Malformed, refusal);
    }

    [Fact]
    public void A_valid_checksum_over_too_few_bytes_is_malformed()
    {
        var data = Encoders.Base58Check.DecodeData(Xpub(DigiByteNetwork.Mainnet));
        var shortened = Encoders.Base58Check.EncodeData(data[..70]);

        Assert.Null(Parse(shortened, DigiByteNetwork.Mainnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.Malformed, refusal);
    }

    [Fact]
    public void An_unknown_version_is_malformed()
    {
        var data = Encoders.Base58Check.DecodeData(Xpub(DigiByteNetwork.Mainnet));
        new byte[] { 0x04, 0xB2, 0x47, 0x46 }.CopyTo(data, 0); // zpub (SLIP-132), not a DigiByte version
        var zpub = Encoders.Base58Check.EncodeData(data);

        Assert.Null(Parse(zpub, DigiByteNetwork.Mainnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.Malformed, refusal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanxpub")]
    [InlineData("dgb1qw508d6qejxtdg4y5r3zarvary0c5xw7kgmn4n9")]
    public void Garbage_is_malformed(string input)
    {
        Assert.Null(Parse(input, DigiByteNetwork.Mainnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.Malformed, refusal);
    }

    [Fact]
    public void Null_is_malformed()
    {
        Assert.Null(HdKeyDerivation.ParseXpubStrict(null, DigiByteNetwork.Mainnet, out var refusal));
        Assert.Equal(HdKeyDerivation.XpubRefusal.Malformed, refusal);
    }

    [Fact]
    public void The_lenient_wallet_parser_still_falls_back_to_the_other_network()
    {
        // Watch-only import relies on this; the strict variant must not change it.
        Assert.NotNull(HdKeyDerivation.ParseXpub(Xpub(DigiByteNetwork.Mainnet), DigiByteNetwork.Testnet));
    }

    private static Network Net(string name) => name == "mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet;
}
