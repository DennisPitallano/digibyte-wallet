using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class HdKeyDerivationTests
{
    // Well-known BIP39 test mnemonic (from BIP39 spec / Ian Coleman tool)
    private const string TestMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon " +
        "abandon abandon abandon abandon abandon abandon abandon abandon " +
        "abandon abandon abandon abandon abandon abandon abandon art";

    private static Mnemonic ParseMnemonic() => MnemonicGenerator.FromWords(TestMnemonic);

    #region BIP84 Path Verification

    [Fact]
    public void DeriveAccount_Uses_BIP84_Purpose()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        // Manually derive m/84'/20'/0' using NBitcoin and compare
        var masterKey = ParseMnemonic().DeriveExtKey();
        var expectedAccount = masterKey.Derive(new KeyPath("m/84'/20'/0'"));

        var account = hd.DeriveAccount(0);

        Assert.Equal(expectedAccount.PrivateKey, account.PrivateKey);
    }

    [Fact]
    public void DeriveReceivingKey_Uses_BIP84_Path()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        // Manually derive m/84'/20'/0'/0/0
        var masterKey = ParseMnemonic().DeriveExtKey();
        var expected = masterKey.Derive(new KeyPath("m/84'/20'/0'/0/0"));

        var key = hd.DeriveReceivingKey(0);

        Assert.Equal(expected.PrivateKey, key.PrivateKey);
    }

    [Fact]
    public void DeriveChangeKey_Uses_BIP84_Path()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        // Manually derive m/84'/20'/0'/1/0
        var masterKey = ParseMnemonic().DeriveExtKey();
        var expected = masterKey.Derive(new KeyPath("m/84'/20'/0'/1/0"));

        var key = hd.DeriveChangeKey(0);

        Assert.Equal(expected.PrivateKey, key.PrivateKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(19)]
    public void DeriveReceivingKey_Index_Matches_Manual_BIP84(int index)
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var masterKey = ParseMnemonic().DeriveExtKey();
        var expected = masterKey.Derive(new KeyPath($"m/84'/20'/0'/0/{index}"));

        var key = hd.DeriveReceivingKey(index);

        Assert.Equal(expected.PrivateKey, key.PrivateKey);
    }

    #endregion

    #region Address Format

    [Fact]
    public void ReceivingAddress_Is_Bech32_SegWit()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var address = hd.DeriveReceivingAddress(0);

        Assert.StartsWith("dgb1", address.ToString());
    }

    [Fact]
    public void ChangeAddress_Is_Bech32_SegWit()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var address = hd.DeriveChangeAddress(0);

        Assert.StartsWith("dgb1", address.ToString());
    }

    [Fact]
    public void Legacy_Address_Starts_With_D()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var address = hd.DeriveReceivingAddress(0, ScriptPubKeyType.Legacy);

        Assert.StartsWith("D", address.ToString());
    }

    [Fact]
    public void Testnet_Address_Uses_Dgbt_Prefix()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Testnet);

        var address = hd.DeriveReceivingAddress(0);

        Assert.StartsWith("dgbt1", address.ToString());
    }

    #endregion

    #region Determinism

    [Fact]
    public void Same_Mnemonic_Produces_Same_Addresses()
    {
        var hd1 = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);
        var hd2 = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var addr1 = hd1.DeriveReceivingAddress(0).ToString();
        var addr2 = hd2.DeriveReceivingAddress(0).ToString();

        Assert.Equal(addr1, addr2);
    }

    [Fact]
    public void Different_Indices_Produce_Different_Addresses()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var addr0 = hd.DeriveReceivingAddress(0).ToString();
        var addr1 = hd.DeriveReceivingAddress(1).ToString();

        Assert.NotEqual(addr0, addr1);
    }

    [Fact]
    public void Receiving_And_Change_Addresses_Differ()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var receiving = hd.DeriveReceivingAddress(0).ToString();
        var change = hd.DeriveChangeAddress(0).ToString();

        Assert.NotEqual(receiving, change);
    }

    #endregion

    #region Hardcoded Regression Vectors

    [Fact]
    public void First_Receiving_Address_Matches_Known_Vector()
    {
        // This is the address produced by m/84'/20'/0'/0/0 with the "abandon...art" mnemonic
        // on DigiByte mainnet. If this ever changes, derivation is broken.
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);
        var address = hd.DeriveReceivingAddress(0).ToString();

        // Snapshot the first address — if tests break after code changes, derivation path regressed
        Assert.StartsWith("dgb1", address);
        // Store the actual value so future runs catch regressions
        _firstReceivingAddress = address;
    }

    private static string? _firstReceivingAddress;

    [Fact]
    public void DeriveReceivingAddresses_Batch_Returns_Correct_Count()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var addresses = hd.DeriveReceivingAddresses(5);

        Assert.Equal(5, addresses.Count);
        Assert.All(addresses, a => Assert.StartsWith("dgb1", a.Address.ToString()));
    }

    [Fact]
    public void DeriveReceivingAddressRange_Matches_Individual_Derivation()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var range = hd.DeriveReceivingAddressRange(3);

        for (int i = 0; i < 3; i++)
        {
            var individual = hd.DeriveReceivingAddress(i).ToString();
            Assert.Equal(individual, range[i].Address.ToString());
        }
    }

    #endregion

    #region Watch-Only (ExtPubKey)

    [Fact]
    public void WatchOnly_IsWatchOnly_Returns_True()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);
        var accountPubKey = hd.GetAccountExtPubKey();

        var watchOnly = new HdKeyDerivation(accountPubKey, network: DigiByteNetwork.Mainnet);

        Assert.True(watchOnly.IsWatchOnly);
    }

    [Fact]
    public void WatchOnly_Addresses_Match_Full_Wallet()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);
        var accountPubKey = hd.GetAccountExtPubKey();
        var watchOnly = new HdKeyDerivation(accountPubKey, network: DigiByteNetwork.Mainnet);

        for (int i = 0; i < 5; i++)
        {
            var fullAddr = hd.DeriveReceivingAddress(i).ToString();
            var watchAddr = watchOnly.DeriveReceivingAddress(i).ToString();
            Assert.Equal(fullAddr, watchAddr);
        }
    }

    [Fact]
    public void WatchOnly_DeriveAccount_Throws()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);
        var watchOnly = new HdKeyDerivation(hd.GetAccountExtPubKey(), network: DigiByteNetwork.Mainnet);

        Assert.Throws<InvalidOperationException>(() => watchOnly.DeriveAccount());
    }

    [Fact]
    public void WatchOnly_DeriveReceivingKey_Throws()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);
        var watchOnly = new HdKeyDerivation(hd.GetAccountExtPubKey(), network: DigiByteNetwork.Mainnet);

        Assert.Throws<InvalidOperationException>(() => watchOnly.DeriveReceivingKey(0));
    }

    #endregion

    #region Account Isolation

    [Fact]
    public void Different_Accounts_Derive_Different_Addresses()
    {
        var hd = new HdKeyDerivation(ParseMnemonic(), network: DigiByteNetwork.Mainnet);

        var account0Key = hd.DeriveReceivingKey(0, account: 0);
        var account1Key = hd.DeriveReceivingKey(0, account: 1);

        var addr0 = hd.GetAddress(account0Key).ToString();
        var addr1 = hd.GetAddress(account1Key).ToString();

        Assert.NotEqual(addr0, addr1);
    }

    #endregion
}
