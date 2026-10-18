using System.Reflection;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Tests;

/// <summary>T011: DigiDollar model invariants from data-model.md.</summary>
public class DigiDollarModelsTests
{
    private static NetworkHealth Health(
        int node = 1000, int? oracle = 995, int? net = 1000, long? price = 6310, bool err = false, bool freeze = false) =>
        new()
        {
            Available = true,
            NodeHeight = node,
            OracleHeight = oracle,
            NetworkHeight = net,
            OraclePriceMicroUsd = price,
            ErrActive = err,
            VolatilityFreeze = freeze,
        };

    [Theory]
    [InlineData(1000, 980, false)] // exactly 20 blocks behind: still fresh
    [InlineData(1000, 979, true)]  // 21 blocks behind: stale
    [InlineData(1000, 1000, false)]
    public void OracleStale_Boundary_Is_Greater_Than_20_Blocks(int node, int oracle, bool stale) =>
        Assert.Equal(stale, Health(node: node, oracle: oracle).IsOracleStale);

    [Theory]
    [InlineData(1006, false)] // exactly 6 behind: not lagging
    [InlineData(1007, true)]  // 7 behind: lagging
    [InlineData(1000, false)]
    public void NodeLagging_Boundary_Is_Greater_Than_6_Blocks(int networkHeight, bool lagging) =>
        Assert.Equal(lagging, Health(net: networkHeight).IsNodeLagging);

    [Fact]
    public void UnknownHeights_AreNotStaleOrLagging()
    {
        var h = Health(oracle: null, net: null);
        Assert.False(h.IsOracleStale);
        Assert.False(h.IsNodeLagging);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void NoUsablePrice_IsUnavailable_AndDegraded(long? price)
    {
        var h = Health(price: price);
        Assert.False(h.OraclePriceAvailable);
        Assert.True(h.IsDegraded);
    }

    [Fact]
    public void Healthy_IsNotDegraded() => Assert.False(Health().IsDegraded);

    [Fact]
    public void Degraded_Rules_ErrFreezeStale()
    {
        Assert.True(Health(err: true).IsDegraded);
        Assert.True(Health(freeze: true).IsDegraded);
        Assert.True(Health(node: 1000, oracle: 900).IsDegraded);
        Assert.False(Health(net: 1500).IsDegraded); // node lag alone is a banner, not a degraded-send state
    }

    [Fact]
    public void Label_Over_64_Chars_IsRejected_64_IsAccepted()
    {
        DigiDollarAddress Make(string label) => new()
        {
            WalletId = "w", Address = "DDx", Chain = DigiDollarChain.Receive, Index = 0,
            CreatedAt = DateTimeOffset.UnixEpoch, Label = label,
        };

        Assert.Equal(64, Make(new string('a', 64)).Label!.Length);
        Assert.Throws<ArgumentException>(() => Make(new string('a', 65)));
    }

    [Fact]
    public void Utxo_Spendable_Only_When_Confirmed()
    {
        var unconf = new DigiDollarUtxo("t", 0, "DDx", 100, "5120", 0, 0);
        var conf = unconf with { Confirmations = 1 };
        Assert.False(unconf.IsSpendable);
        Assert.True(conf.IsSpendable);
    }

    [Fact]
    public void NewAccount_Starts_At_Index_Zero_And_Unscanned()
    {
        var a = DigiDollarAccount.Create("w1", DdNetwork.Testnet, DateTimeOffset.UnixEpoch);
        Assert.Equal(0, a.NextReceiveIndex);
        Assert.Equal(0, a.NextChangeIndex);
        Assert.False(a.ScanComplete);
        Assert.Null(a.LastScanAt);
    }

    /// <summary>
    /// FR-008 / Constitution I: no DigiDollar model may be able to carry a private key, seed, mnemonic or WIF.
    /// </summary>
    [Fact]
    public void NoModelHasASecretBearingMember()
    {
        var banned = new[] { "priv", "seed", "mnemonic", "wif", "xprv", "secret", "passphrase" };
        var types = typeof(DigiDollarState).Assembly.GetTypes()
            .Where(t => t.Namespace == "DigiByte.Wallet.Models" && t.Name is
                "DigiDollarAccount" or "DigiDollarAddress" or "DigiDollarUtxo" or "DigiDollarBalance" or
                "DigiDollarTransaction" or "DigiDollarCache" or "DdOutpoint" or "PendingDdSend" or
                "DigiDollarState" or "NetworkHealth")
            .ToList();
        Assert.Equal(10, types.Count);

        foreach (var t in types)
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(m => m.MemberType is MemberTypes.Property or MemberTypes.Field))
                Assert.DoesNotContain(banned, b => m.Name.Contains(b, StringComparison.OrdinalIgnoreCase));
    }
}
