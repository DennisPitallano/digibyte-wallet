using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class HdKeyDerivationWatchOnlyTests
{
    private static readonly Network Network = DigiByteNetwork.Mainnet;

    /// <summary>
    /// Create an HD wallet from a mnemonic, extract the account xpub,
    /// then create a watch-only wallet from it. Both should derive the same addresses.
    /// </summary>
    private static (HdKeyDerivation Full, HdKeyDerivation WatchOnly) CreatePair()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var full = new HdKeyDerivation(mnemonic, network: Network);
        var xpub = full.GetAccountExtPubKey();
        var watchOnly = new HdKeyDerivation(xpub, Network);
        return (full, watchOnly);
    }

    [Fact]
    public void IsWatchOnly_True_ForXpubConstructor()
    {
        var (_, watchOnly) = CreatePair();
        Assert.True(watchOnly.IsWatchOnly);
    }

    [Fact]
    public void IsWatchOnly_False_ForMnemonicConstructor()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        Assert.False(hd.IsWatchOnly);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(19)]
    public void DeriveReceivingAddress_MatchesFullWallet_Segwit(int index)
    {
        var (full, watchOnly) = CreatePair();

        var fullKey = full.DeriveReceivingKey(index);
        var fullAddr = full.GetAddress(fullKey, ScriptPubKeyType.Segwit).ToString();

        var watchAddr = watchOnly.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString();

        Assert.Equal(fullAddr, watchAddr);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void DeriveReceivingAddress_MatchesFullWallet_Legacy(int index)
    {
        var (full, watchOnly) = CreatePair();

        var fullKey = full.DeriveReceivingKey(index);
        var fullAddr = full.GetAddress(fullKey, ScriptPubKeyType.Legacy).ToString();

        var watchAddr = watchOnly.DeriveReceivingAddress(index, ScriptPubKeyType.Legacy).ToString();

        Assert.Equal(fullAddr, watchAddr);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DeriveChangeAddress_MatchesFullWallet(int index)
    {
        var (full, watchOnly) = CreatePair();

        var fullKey = full.DeriveChangeKey(index);
        var fullAddr = full.GetAddress(fullKey, ScriptPubKeyType.Segwit).ToString();

        var watchAddr = watchOnly.DeriveChangeAddress(index, ScriptPubKeyType.Segwit).ToString();

        Assert.Equal(fullAddr, watchAddr);
    }

    [Fact]
    public void DeriveReceivingAddressRange_MatchesFullWallet()
    {
        var (full, watchOnly) = CreatePair();

        var fullAddrs = full.DeriveReceivingAddresses(5);
        var watchAddrs = watchOnly.DeriveReceivingAddressRange(5);

        Assert.Equal(fullAddrs.Count, watchAddrs.Count);
        for (int i = 0; i < fullAddrs.Count; i++)
        {
            Assert.Equal(fullAddrs[i].Index, watchAddrs[i].Index);
            Assert.Equal(fullAddrs[i].Address.ToString(), watchAddrs[i].Address.ToString());
        }
    }

    [Fact]
    public void DeriveReceivingKey_Throws_ForWatchOnly()
    {
        var (_, watchOnly) = CreatePair();
        var ex = Assert.Throws<InvalidOperationException>(() => watchOnly.DeriveReceivingKey(0));
        Assert.Contains("watch-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeriveChangeKey_Throws_ForWatchOnly()
    {
        var (_, watchOnly) = CreatePair();
        var ex = Assert.Throws<InvalidOperationException>(() => watchOnly.DeriveChangeKey(0));
        Assert.Contains("watch-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeriveAccount_Throws_ForWatchOnly()
    {
        var (_, watchOnly) = CreatePair();
        var ex = Assert.Throws<InvalidOperationException>(() => watchOnly.DeriveAccount());
        Assert.Contains("watch-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeriveDigiIdKey_Throws_ForWatchOnly()
    {
        var (_, watchOnly) = CreatePair();
        var ex = Assert.Throws<InvalidOperationException>(() => watchOnly.DeriveDigiIdKey(42));
        Assert.Contains("watch-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeriveReceivingAddresses_Throws_ForWatchOnly()
    {
        var (_, watchOnly) = CreatePair();
        var ex = Assert.Throws<InvalidOperationException>(() => watchOnly.DeriveReceivingAddresses(3));
        Assert.Contains("watch-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAccountExtPubKey_Works_ForWatchOnly()
    {
        var (full, watchOnly) = CreatePair();

        var fullXpub = full.GetAccountExtPubKey().ToString(Network);
        var watchXpub = watchOnly.GetAccountExtPubKey().ToString(Network);

        Assert.Equal(fullXpub, watchXpub);
    }

    [Fact]
    public void ParseXpub_ValidXpub_ReturnsExtPubKey()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        var xpubStr = hd.GetAccountExtPubKey().ToString(Network);

        var parsed = HdKeyDerivation.ParseXpub(xpubStr, Network);

        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("notanxpub")]
    [InlineData("xpub123456")]
    [InlineData("   ")]
    public void ParseXpub_Invalid_ReturnsNull(string input)
    {
        var result = HdKeyDerivation.ParseXpub(input, Network);
        Assert.Null(result);
    }

    [Fact]
    public void ParseXpub_TrimsWhitespace()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        var xpubStr = "  " + hd.GetAccountExtPubKey().ToString(Network) + "  ";

        var parsed = HdKeyDerivation.ParseXpub(xpubStr, Network);

        Assert.NotNull(parsed);
    }

    [Fact]
    public void WatchOnly_ReceivingAddresses_AreUnique()
    {
        var (_, watchOnly) = CreatePair();

        var addresses = new HashSet<string>();
        for (int i = 0; i < 20; i++)
        {
            var addr = watchOnly.DeriveReceivingAddress(i).ToString();
            Assert.True(addresses.Add(addr), $"Duplicate address at index {i}");
        }
    }

    [Fact]
    public void WatchOnly_ReceivingAndChangeAddresses_AreDistinct()
    {
        var (_, watchOnly) = CreatePair();

        var receiving = watchOnly.DeriveReceivingAddress(0).ToString();
        var change = watchOnly.DeriveChangeAddress(0).ToString();

        Assert.NotEqual(receiving, change);
    }
}
