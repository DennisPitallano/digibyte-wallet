using DigiByte.Crypto.Addresses;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// T007: DigiDollar address encode/decode. A DD address is Base58Check(2 version bytes || 32-byte
/// Taproot OUTPUT key); the output key is used as-is in the value-0 P2TR script <c>OP_1 &lt;key&gt;</c>
/// (research R1/R2/R3).
/// </summary>
public class DigiDollarAddressTests
{
    // Real x-only keys (on the curve), derived from fixed secrets, so decode-side curve validation passes.
    private static byte[] ValidKey(int seed) =>
        new Key(Enumerable.Range(seed, 32).Select(i => (byte)i).ToArray()).PubKey.TaprootInternalKey.ToBytes();

    private static readonly byte[] KeyA = ValidKey(1);
    private static readonly byte[] KeyB = ValidKey(100);

    private static readonly Base58CheckEncoder B58 = new();

    [Theory]
    [InlineData("mainnet", "DD")]
    [InlineData("testnet", "TD")]
    public void Encode_ProducesNetworkPrefix_AndIsValid(string network, string prefix)
    {
        var address = DigiDollarAddressValidator.Encode(KeyA, network);

        Assert.StartsWith(prefix, address, StringComparison.Ordinal);
        Assert.True(DigiDollarAddressValidator.IsValid(address, network));
        Assert.Equal(network, DigiDollarAddressValidator.DetectNetwork(address));
    }

    [Theory]
    [InlineData("mainnet")]
    [InlineData("testnet")]
    public void EncodeThenDecode_RoundTrips_TheOutputKey(string network)
    {
        var address = DigiDollarAddressValidator.Encode(KeyB, network);

        Assert.True(DigiDollarAddressValidator.TryDecode(address, network, out var detected, out var key));
        Assert.Equal(network, detected);
        Assert.Equal(KeyB, key);
    }

    [Fact]
    public void Encode_OfTaprootPubKey_MatchesByteOverload()
    {
        var pub = new TaprootPubKey(KeyA);
        Assert.Equal(
            DigiDollarAddressValidator.Encode(KeyA, "mainnet"),
            DigiDollarAddressValidator.Encode(pub, "mainnet"));
    }

    [Fact]
    public void ToScriptPubKey_IsOp1_PushOfTheKey()
    {
        var address = DigiDollarAddressValidator.Encode(KeyA, "testnet");

        var script = DigiDollarAddressValidator.ToScriptPubKey(address, "testnet");

        Assert.Equal("5120" + Convert.ToHexString(KeyA).ToLowerInvariant(), script.ToHex());
    }

    [Fact]
    public void Different_Keys_Give_Different_Addresses()
    {
        Assert.NotEqual(
            DigiDollarAddressValidator.Encode(KeyA, "mainnet"),
            DigiDollarAddressValidator.Encode(KeyB, "mainnet"));
    }

    // ---------- negative paths (Constitution III) ----------

    [Fact]
    public void Rejects_DgbBech32Address()
    {
        // Real-shaped DGB bech32 / legacy strings must never validate as DD.
        Assert.False(DigiDollarAddressValidator.IsValid("dgb1qw508d6qejxtdg4y5r3zarvary0c5xw7kz8x4cd", "mainnet"));
        Assert.False(DigiDollarAddressValidator.IsValid("dgbt1qw508d6qejxtdg4y5r3zarvary0c5xw7kxxxxxx", "testnet"));
        Assert.False(DigiDollarAddressValidator.TryDecode("dgb1qw508d6qejxtdg4y5r3zarvary0c5xw7kz8x4cd", "mainnet", out _, out _));
    }

    [Fact]
    public void Rejects_WrongNetwork_BothDirections()
    {
        var main = DigiDollarAddressValidator.Encode(KeyA, "mainnet");
        var test = DigiDollarAddressValidator.Encode(KeyA, "testnet");

        Assert.False(DigiDollarAddressValidator.IsValid(main, "testnet"));
        Assert.False(DigiDollarAddressValidator.IsValid(test, "mainnet"));
        Assert.False(DigiDollarAddressValidator.TryDecode(main, "testnet", out _, out _));
        Assert.False(DigiDollarAddressValidator.TryDecode(test, "mainnet", out _, out _));
    }

    [Fact]
    public void Rejects_BadChecksum()
    {
        var address = DigiDollarAddressValidator.Encode(KeyA, "mainnet");
        var last = address[^1];
        var replacement = last == '2' ? '3' : '2';
        var corrupted = address[..^1] + replacement;

        Assert.False(DigiDollarAddressValidator.IsValid(corrupted, "mainnet"));
        Assert.False(DigiDollarAddressValidator.TryDecode(corrupted, "mainnet", out _, out _));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void Rejects_WrongBodyLength(int bodyLength)
    {
        // Correct network version bytes and a valid Base58Check checksum, but the wrong key length.
        var payload = new byte[] { 0x52, 0x85 }.Concat(new byte[bodyLength]).ToArray();
        var address = B58.EncodeData(payload);

        Assert.False(DigiDollarAddressValidator.IsValid(address, "mainnet"));
        Assert.False(DigiDollarAddressValidator.TryDecode(address, "mainnet", out _, out _));
    }

    [Fact]
    public void Rejects_KeyThatIsNotAPointOnTheCurve()
    {
        // Valid Base58Check and length, but 0xFF..FF is >= the field prime, so no such x-only key exists.
        // Funds sent to it would be unspendable, so it must never validate.
        var payload = new byte[] { 0x52, 0x85 }.Concat(Enumerable.Repeat((byte)0xFF, 32)).ToArray();
        var address = B58.EncodeData(payload);

        Assert.False(DigiDollarAddressValidator.IsValid(address, "mainnet"));
        Assert.False(DigiDollarAddressValidator.TryDecode(address, "mainnet", out _, out _));
    }

    [Fact]
    public void Rejects_SurroundingWhitespace()
    {
        var address = DigiDollarAddressValidator.Encode(KeyA, "mainnet");

        Assert.False(DigiDollarAddressValidator.IsValid(" " + address, "mainnet"));
        Assert.False(DigiDollarAddressValidator.IsValid(address + " ", "mainnet"));
        Assert.False(DigiDollarAddressValidator.IsValid(address + "\n", "mainnet"));
        Assert.False(DigiDollarAddressValidator.TryDecode(" " + address, "mainnet", out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_EmptyOrNull(string? value)
    {
        Assert.False(DigiDollarAddressValidator.IsValid(value, "mainnet"));
        Assert.False(DigiDollarAddressValidator.TryDecode(value, "mainnet", out _, out _));
    }

    [Fact]
    public void Encode_RejectsBadKeyLength_AndUnknownNetwork()
    {
        Assert.Throws<ArgumentException>(() => DigiDollarAddressValidator.Encode(new byte[31], "mainnet"));
        Assert.Throws<ArgumentException>(() => DigiDollarAddressValidator.Encode(new byte[33], "mainnet"));
        Assert.Throws<ArgumentException>(() => DigiDollarAddressValidator.Encode(KeyA, "signet"));
        Assert.Throws<ArgumentException>(() => DigiDollarAddressValidator.Encode(KeyA, null));
    }

    [Fact]
    public void ToScriptPubKey_ThrowsForInvalidAddress()
    {
        Assert.Throws<ArgumentException>(() => DigiDollarAddressValidator.ToScriptPubKey("not-an-address", "mainnet"));
    }
}
