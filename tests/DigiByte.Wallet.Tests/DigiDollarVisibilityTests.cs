using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T013: when DigiDollar is visible, sendable, or replaced by a notice (FR-001, FR-002, FR-022, FR-029, FR-030;
/// contracts/wallet-ui-contract.md "Effective visibility").
/// </summary>
public class DigiDollarVisibilityTests
{
    private static DdVisibility Eval(
        bool view = true, bool send = true, string network = "mainnet", DdWalletKind kind = DdWalletKind.Seed,
        bool reachable = true, bool active = true, bool used = false) =>
        DigiDollarVisibility.Evaluate(new DigiDollarFlags(view, send), network, kind, reachable, active, used);

    // ---------- visibility ----------

    [Theory]
    [InlineData("mainnet")]
    [InlineData("testnet")]
    public void SeedWallet_OnSupportedNetwork_WithViewOn_IsVisible(string network)
    {
        var v = Eval(network: network);
        Assert.True(v.Visible);
        Assert.Equal(DdHiddenReason.None, v.HiddenReason);
        Assert.False(v.ShowUnavailableLine);
        Assert.False(v.ShowUnsupportedNotice);
    }

    [Fact]
    public void ViewOff_HidesEverything()
    {
        var v = Eval(view: false);
        Assert.False(v.Visible);
        Assert.False(v.SendVisible);
        Assert.Equal(DdHiddenReason.FlagOff, v.HiddenReason);
    }

    [Theory]
    [InlineData("regtest")]
    [InlineData("signet")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsupportedNetwork_HidesEverything_LikeTheFlagBeingOff(string? network)
    {
        // FR-029: on regtest the DD section must not appear at all, not even the neutral line or a notice.
        var v = Eval(network: network!, used: true);
        Assert.False(v.Visible);
        Assert.False(v.SendVisible);
        Assert.False(v.ShowUnavailableLine);
        Assert.False(v.ShowUnsupportedNotice);
        Assert.Equal(DdHiddenReason.Network, v.HiddenReason);
    }

    [Theory]
    [InlineData(DdWalletKind.PrivateKey)]
    [InlineData(DdWalletKind.WatchOnly)]
    [InlineData(DdWalletKind.Multisig)]
    public void NonSeedWallets_GetTheNotice_NotBalancesOrActions(DdWalletKind kind)
    {
        // FR-030
        var v = Eval(kind: kind);
        Assert.False(v.Visible);
        Assert.False(v.SendVisible);
        Assert.True(v.ShowUnsupportedNotice);
        Assert.Equal(DdHiddenReason.WalletType, v.HiddenReason);
    }

    [Fact]
    public void NoActiveWallet_IsHidden_WithoutANotice()
    {
        var v = Eval(kind: DdWalletKind.None);
        Assert.False(v.Visible);
        Assert.False(v.ShowUnsupportedNotice);
    }

    [Fact]
    public void NonSeedWallet_WithViewOff_ShowsNothing()
    {
        var v = Eval(view: false, kind: DdWalletKind.WatchOnly);
        Assert.False(v.ShowUnsupportedNotice);
        Assert.False(v.ShowUnavailableLine);
    }

    // ---------- the neutral "temporarily unavailable" line (FR-002) ----------

    [Theory]
    [InlineData(true, DdWalletKind.Seed, "mainnet", true)]
    [InlineData(true, DdWalletKind.Seed, "testnet", true)]
    [InlineData(false, DdWalletKind.Seed, "mainnet", false)]      // never used DigiDollar on this device
    [InlineData(true, DdWalletKind.WatchOnly, "mainnet", false)]  // not a seed wallet
    [InlineData(true, DdWalletKind.Seed, "regtest", false)]
    public void UnavailableLine_OnlyWhenViewIsOff_AndTheDeviceUsedDigiDollar_OnASeedWallet(
        bool used, DdWalletKind kind, string network, bool expected)
    {
        var v = Eval(view: false, kind: kind, network: network, used: used);
        Assert.Equal(expected, v.ShowUnavailableLine);
    }

    [Fact]
    public void UnavailableLine_NeverShows_WhenViewIsOn() =>
        Assert.False(Eval(view: true, used: true).ShowUnavailableLine);

    // ---------- send ----------

    [Fact]
    public void Send_RequiresSendFlag_Reachability_AndAnActiveDeployment()
    {
        Assert.True(Eval().SendVisible);
        Assert.False(Eval(send: false).SendVisible);
        Assert.False(Eval(reachable: false).SendVisible);
        Assert.False(Eval(active: false).SendVisible);
    }

    [Fact]
    public void ViewCanBeEnabledWithoutSend()
    {
        // FR-022: independent switches
        var v = Eval(view: true, send: false);
        Assert.True(v.Visible);
        Assert.False(v.SendVisible);
    }

    [Fact]
    public void SendFlagAlone_DoesNotMakeAnythingVisible()
    {
        var v = Eval(view: false, send: true);
        Assert.False(v.Visible);
        Assert.False(v.SendVisible);
    }

    [Fact]
    public void SendIsNeverVisible_WhenTheSectionIsNot()
    {
        Assert.False(Eval(kind: DdWalletKind.PrivateKey, send: true).SendVisible);
        Assert.False(Eval(network: "regtest", send: true).SendVisible);
    }

    // ---------- wallet kind mapping ----------

    [Theory]
    [InlineData("hd", DdWalletKind.Seed)]
    [InlineData("HD", DdWalletKind.Seed)]
    [InlineData("privatekey", DdWalletKind.PrivateKey)]
    [InlineData("xpub", DdWalletKind.WatchOnly)]
    [InlineData("multisig", DdWalletKind.Multisig)]
    [InlineData("something-new", DdWalletKind.PrivateKey)] // unknown types are NOT assumed to be seed-based
    [InlineData(null, DdWalletKind.None)]
    [InlineData("", DdWalletKind.None)]
    public void WalletTypeString_MapsToKind(string? walletType, DdWalletKind expected) =>
        Assert.Equal(expected, DigiDollarVisibility.KindFromWalletType(walletType));
}
