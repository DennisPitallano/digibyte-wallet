using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.KeyGeneration;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// T009: DigiDollar key derivation. DD keys live on the BIP86 Taproot path
/// m/86'/{coin}'/0'/{chain}/{index}; the DD address encodes the BIP341 key-path-tweaked OUTPUT key
/// (no script tree), exactly like Core's CreateDigiDollarP2TR (research R2).
/// </summary>
public class DigiDollarKeyDerivationTests
{
    // Official BIP86 test mnemonic and vectors (https://github.com/bitcoin/bips/blob/master/bip-0086.mediawiki).
    private const string Bip86Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static Mnemonic Words(string w = Bip86Mnemonic) => new(w, Wordlist.English);

    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    // The BIP86 vectors use coin type 0, so the coin type must be an explicit parameter.
    [Theory]
    [InlineData(DigiDollarChain.Receive, 0,
        "cc8a4bc64d897bddc5fbc2f670f7a8ba0b386779106cf1223c6fc5d7cd6fc115",
        "a60869f0dbcf1dc659c9cecbaf8050135ea9e8cdc487053f1dc6880949dc684c")]
    [InlineData(DigiDollarChain.Receive, 1,
        "83dfe85a3151d2517290da461fe2815591ef69f2b18a2ce63f01697a8b313145",
        "a82f29944d65b86ae6b5e5cc75e294ead6c59391a1edc5e016e3498c67fc7bbb")]
    [InlineData(DigiDollarChain.Change, 0,
        "399f1b2f4393f29a18c937859c5dd8a77350103157eb880f02e8c08214277cef",
        "882d74e5d0572d5a816cef0041a96b6c1de832f6f9676d9605c44d5e9a97d3dc")]
    public void Matches_Official_Bip86_Vectors(DigiDollarChain chain, int index, string internalKeyHex, string outputKeyHex)
    {
        var dd = new DigiDollarKeyDerivation(Words(), network: "mainnet", coinType: 0);

        var key = dd.Derive(chain, index);

        Assert.Equal(internalKeyHex, Hex(key.InternalKey.ToBytes()));
        Assert.Equal(outputKeyHex, Hex(key.OutputKey.ToBytes()));
    }

    [Fact]
    public void Mainnet_DefaultCoinType_Is20()
    {
        var dd = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        Assert.Equal("m/86'/20'/0'/0/0", dd.Derive(DigiDollarChain.Receive, 0).Path);
        Assert.Equal("m/86'/20'/0'/1/3", dd.Derive(DigiDollarChain.Change, 3).Path);
    }

    [Fact]
    public void Testnet_DefaultCoinType_FollowsCoreConvention_1()
    {
        // Open decision R2/T002: Core descriptor wallets use coin type 1 on testnet. Confirm against a
        // Core-generated testnet26 vector before shipping; the coin type stays an explicit parameter.
        var dd = new DigiDollarKeyDerivation(Words(), network: "testnet");
        Assert.Equal("m/86'/1'/0'/0/0", dd.Derive(DigiDollarChain.Receive, 0).Path);
    }

    [Fact]
    public void Address_EncodesTheTweakedOutputKey_AndValidates()
    {
        var dd = new DigiDollarKeyDerivation(Words(), network: "testnet");
        var key = dd.Derive(DigiDollarChain.Receive, 0);

        var address = dd.GetAddress(key);

        Assert.StartsWith("TD", address);
        Assert.True(DigiDollarAddressValidator.IsValid(address, "testnet"));
        Assert.True(DigiDollarAddressValidator.TryDecode(address, "testnet", out _, out var decoded));
        Assert.Equal(key.OutputKey.ToBytes(), decoded);
        Assert.NotEqual(key.InternalKey.ToBytes(), decoded); // the address is the OUTPUT key, not the internal key
    }

    [Fact]
    public void MainnetAddress_StartsWithDD()
    {
        var dd = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        Assert.StartsWith("DD", dd.GetAddress(dd.Derive(DigiDollarChain.Receive, 0)));
    }

    [Fact]
    public void Signing_WithDerivedKey_VerifiesAgainstTheOutputKey()
    {
        var dd = new DigiDollarKeyDerivation(Words(), network: "testnet");
        var key = dd.Derive(DigiDollarChain.Change, 2);
        var hash = new uint256("1111111111111111111111111111111111111111111111111111111111111111");

        var sig = key.SignKeySpend(hash);

        Assert.True(key.OutputKey.VerifySignature(hash, sig.SchnorrSignature));
    }

    [Fact]
    public void Derivation_IsDeterministic_AndIndexesAreDistinct()
    {
        var a = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        var b = new DigiDollarKeyDerivation(Words(), network: "mainnet");

        Assert.Equal(a.Derive(DigiDollarChain.Receive, 5).OutputKey, b.Derive(DigiDollarChain.Receive, 5).OutputKey);
        Assert.NotEqual(a.Derive(DigiDollarChain.Receive, 0).OutputKey, a.Derive(DigiDollarChain.Receive, 1).OutputKey);
        Assert.NotEqual(a.Derive(DigiDollarChain.Receive, 0).OutputKey, a.Derive(DigiDollarChain.Change, 0).OutputKey);
    }

    [Fact]
    public void Passphrase_ChangesTheKeys()
    {
        var plain = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        var salted = new DigiDollarKeyDerivation(Words(), passphrase: "salt", network: "mainnet");

        Assert.NotEqual(plain.Derive(DigiDollarChain.Receive, 0).OutputKey, salted.Derive(DigiDollarChain.Receive, 0).OutputKey);
    }

    [Fact]
    public void DifferentNetworks_UseDifferentCoinTypes_SoDifferentKeys()
    {
        var main = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        var test = new DigiDollarKeyDerivation(Words(), network: "testnet");

        Assert.NotEqual(main.Derive(DigiDollarChain.Receive, 0).OutputKey, test.Derive(DigiDollarChain.Receive, 0).OutputKey);
    }

    // ---------- negative paths (Constitution III) ----------

    [Fact]
    public void Rejects_NullMnemonic() =>
        Assert.Throws<ArgumentNullException>(() => new DigiDollarKeyDerivation((Mnemonic)null!, network: "mainnet"));

    [Fact]
    public void Rejects_NullMasterKey() =>
        Assert.Throws<ArgumentNullException>(() => new DigiDollarKeyDerivation((ExtKey)null!, network: "mainnet"));

    [Fact]
    public void FromMasterKey_DerivesTheSameKeysAsFromTheMnemonic()
    {
        var mnemonic = Words();
        var fromMnemonic = new DigiDollarKeyDerivation(mnemonic, network: "testnet");
        var fromMaster = new DigiDollarKeyDerivation(mnemonic.DeriveExtKey(), network: "testnet");

        Assert.Equal(
            fromMnemonic.Derive(DigiDollarChain.Receive, 3).OutputKey,
            fromMaster.Derive(DigiDollarChain.Receive, 3).OutputKey);
        Assert.Equal(fromMnemonic.StorageKeyMaterial(), fromMaster.StorageKeyMaterial());
    }

    [Fact]
    public void StorageKeyMaterial_DiffersPerSeed()
    {
        var a = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        var b = new DigiDollarKeyDerivation(Words(), passphrase: "x", network: "mainnet");
        Assert.NotEqual(a.StorageKeyMaterial(), b.StorageKeyMaterial());
    }

    [Theory]
    [InlineData("regtest")]
    [InlineData("signet")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_UnsupportedNetwork(string? network) =>
        Assert.Throws<ArgumentException>(() => new DigiDollarKeyDerivation(Words(), network: network!));

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Rejects_NegativeIndex(int index)
    {
        var dd = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        Assert.Throws<ArgumentOutOfRangeException>(() => dd.Derive(DigiDollarChain.Receive, index));
    }

    [Fact]
    public void Rejects_UndefinedChain()
    {
        var dd = new DigiDollarKeyDerivation(Words(), network: "mainnet");
        Assert.Throws<ArgumentOutOfRangeException>(() => dd.Derive((DigiDollarChain)7, 0));
    }
}
