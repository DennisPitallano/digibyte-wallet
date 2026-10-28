using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T023: the status wording and banner rule (specs/005-wallet-own-node-source/contracts/status-ui.md, research R8).
/// </summary>
public class NodeSourceStatusTests
{
    private static NodeSourceStatusView View(
        NodeSourceState state, NodeNotReadyReason reason = NodeNotReadyReason.None, bool wasActive = false, bool allFailed = false) =>
        NodeSourceStatus.Compute(state, reason, wasActive, allFailed);

    // ---------- visibility ----------

    [Fact]
    public void FeatureOff_NothingIsShown_EvenIfAllSourcesFailed()
    {
        var v = View(NodeSourceState.Disabled, NodeNotReadyReason.Disabled, allFailed: true);

        Assert.False(v.Visible);
        Assert.False(v.ShowBanner);
        Assert.Equal(NodeSourceDot.None, v.Dot);
    }

    [Fact]
    public void FeatureOn_TheIndicatorIsVisible()
    {
        Assert.True(View(NodeSourceState.NotReady, NodeNotReadyReason.Syncing).Visible);
    }

    // ---------- own node active ----------

    [Fact]
    public void OwnNodeActive_IsGreen_WithThePrivacyLine_AndNoBanner()
    {
        var v = View(NodeSourceState.Active, wasActive: true);

        Assert.Equal(NodeSourceDot.Green, v.Dot);
        Assert.Contains("run by the wallet team", v.Detail);
        Assert.Contains("Balances come from public block explorers", v.Detail);
        Assert.NotNull(v.PrivacyLine);
        Assert.Contains("see the public transactions and addresses", v.PrivacyLine!);
        Assert.Contains("keys never leave your device", v.PrivacyLine!);
        Assert.False(v.ShowBanner);
    }

    [Theory]
    [InlineData(NodeSourceState.Unavailable, NodeNotReadyReason.Unreachable)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Syncing)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Starting)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.HeightDrift)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Version)]
    [InlineData(NodeSourceState.Stabilizing, NodeNotReadyReason.None)]
    public void PrivacyLine_OnlyWhenTheOwnNodeIsActive(NodeSourceState state, NodeNotReadyReason reason)
    {
        Assert.Null(View(state, reason).PrivacyLine);
        Assert.Null(View(state, reason, wasActive: true).PrivacyLine);
    }

    // ---------- explorers in use: the three informational cases ----------

    [Theory]
    [InlineData(NodeNotReadyReason.Syncing)]
    [InlineData(NodeNotReadyReason.Starting)]
    public void NodeSyncing_IsInformational_NeutralDot_NoBanner(NodeNotReadyReason reason)
    {
        var v = View(NodeSourceState.NotReady, reason);

        Assert.Equal(NodeSourceDot.Neutral, v.Dot);
        Assert.Contains("Using public block explorers", v.Detail);
        Assert.Contains("still syncing", v.Detail);
        Assert.False(v.ShowBanner);
    }

    [Theory]
    [InlineData(NodeNotReadyReason.Unreachable)]
    [InlineData(NodeNotReadyReason.Malformed)]
    public void NodeUnreachable_SaysNotAvailable_NoBannerIfItNeverWorked(NodeNotReadyReason reason)
    {
        var v = View(NodeSourceState.Unavailable, reason);

        Assert.Contains("Using public block explorers", v.Detail);
        Assert.Contains("not available right now", v.Detail);
        Assert.False(v.ShowBanner);
    }

    [Fact]
    public void NodeBehindOrOnAnotherChain_SaysOutOfStep()
    {
        var v = View(NodeSourceState.NotReady, NodeNotReadyReason.HeightDrift);

        Assert.Contains("out of step with the network", v.Detail);
        Assert.False(v.ShowBanner);
    }

    [Theory]
    [InlineData(NodeNotReadyReason.Version)]
    [InlineData(NodeNotReadyReason.Txindex)]
    [InlineData(NodeNotReadyReason.Unhealthy)]
    [InlineData(NodeNotReadyReason.ReadFailures)]
    public void OtherNotReadyReasons_SayNotAvailable(NodeNotReadyReason reason)
    {
        var v = View(NodeSourceState.NotReady, reason);

        Assert.Contains("not available right now", v.Detail);
    }

    [Fact]
    public void Stabilizing_SaysTheNodeWillBeUsedAutomatically()
    {
        var v = View(NodeSourceState.Stabilizing);

        Assert.Contains("Using public block explorers", v.Detail);
        Assert.Contains("automatically", v.Detail);
        Assert.False(v.ShowBanner);
        Assert.Equal(NodeSourceDot.Neutral, v.Dot);
    }

    // ---------- the banner rule: real degradation only ----------

    [Theory]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Syncing)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.HeightDrift)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.ReadFailures)]
    [InlineData(NodeSourceState.Unavailable, NodeNotReadyReason.Unreachable)]
    [InlineData(NodeSourceState.Stabilizing, NodeNotReadyReason.None)]
    public void NodeThatWasActiveAndFellBack_ShowsTheBanner_AmberDot(NodeSourceState state, NodeNotReadyReason reason)
    {
        var v = View(state, reason, wasActive: true);

        Assert.True(v.ShowBanner);
        Assert.Equal(NodeSourceDot.Amber, v.Dot);
        Assert.Equal("The wallet team's node is unavailable. Using public explorers instead.", v.BannerText);
    }

    [Theory]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Syncing)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Starting)]
    [InlineData(NodeSourceState.Unavailable, NodeNotReadyReason.Unreachable)]
    [InlineData(NodeSourceState.Stabilizing, NodeNotReadyReason.None)]
    public void NodeThatNeverBecameReady_NeverShowsABanner(NodeSourceState state, NodeNotReadyReason reason)
    {
        var v = View(state, reason, wasActive: false);

        Assert.False(v.ShowBanner);
        Assert.Null(v.BannerText);
        Assert.Equal(NodeSourceDot.Neutral, v.Dot);
    }

    // ---------- nothing available ----------

    [Theory]
    [InlineData(NodeSourceState.Active)]
    [InlineData(NodeSourceState.NotReady)]
    [InlineData(NodeSourceState.Unavailable)]
    public void AllSourcesFailed_IsRed_WithTheLastKnownDataBanner(NodeSourceState state)
    {
        var v = View(state, wasActive: true, allFailed: true);

        Assert.Equal(NodeSourceDot.Red, v.Dot);
        Assert.True(v.ShowBanner);
        Assert.Contains("Can't reach the blockchain right now", v.BannerText);
        Assert.Contains("last known balance, which may be out of date", v.BannerText);
        Assert.Contains("Sending is unavailable until the connection returns", v.BannerText);
        Assert.Contains("No data source is reachable", v.Detail);
    }

    [Fact]
    public void AllSourcesFailed_TakesPriorityOverTheNodeFallbackBanner()
    {
        var v = View(NodeSourceState.NotReady, NodeNotReadyReason.Syncing, wasActive: true, allFailed: true);

        Assert.DoesNotContain("wallet team's node is unavailable", v.BannerText);
    }

    [Fact]
    public void Healthy_NoBannerAndNoWarning()
    {
        var v = View(NodeSourceState.Active, wasActive: true, allFailed: false);

        Assert.False(v.ShowBanner);
        Assert.Null(v.BannerText);
    }

    // ---------- copy hygiene ----------

    [Fact]
    public void EveryDetailText_IsPlainLanguage_NoJargonOrInternalNames()
    {
        var states = new (NodeSourceState, NodeNotReadyReason)[]
        {
            (NodeSourceState.Active, NodeNotReadyReason.None),
            (NodeSourceState.Stabilizing, NodeNotReadyReason.None),
            (NodeSourceState.NotReady, NodeNotReadyReason.Syncing),
            (NodeSourceState.NotReady, NodeNotReadyReason.HeightDrift),
            (NodeSourceState.Unavailable, NodeNotReadyReason.Unreachable),
        };

        foreach (var (state, reason) in states)
        {
            var v = View(state, reason);
            foreach (var banned in new[] { "txindex", "UTXO", "RPC", "scantxoutset", "HeightDrift", "ReadFailures", "NotReady" })
                Assert.DoesNotContain(banned, v.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- Deployment page badge ----------

    [Theory]
    [InlineData(NodeSourceState.Disabled, NodeNotReadyReason.Disabled, "Not in use", NodeBadgeTone.Muted)]
    [InlineData(NodeSourceState.Active, NodeNotReadyReason.None, "In use", NodeBadgeTone.Good)]
    [InlineData(NodeSourceState.Stabilizing, NodeNotReadyReason.None, "Checking", NodeBadgeTone.Warn)]
    [InlineData(NodeSourceState.Unavailable, NodeNotReadyReason.Unreachable, "Unreachable", NodeBadgeTone.Warn)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Syncing, "Syncing", NodeBadgeTone.Warn)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Starting, "Syncing", NodeBadgeTone.Warn)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.HeightDrift, "Out of step", NodeBadgeTone.Warn)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Version, "Unavailable", NodeBadgeTone.Warn)]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.ReadFailures, "Unavailable", NodeBadgeTone.Warn)]
    public void DeploymentBadge_ReflectsTheLiveState(NodeSourceState state, NodeNotReadyReason reason, string label, NodeBadgeTone tone)
    {
        var badge = NodeSourceStatus.DeploymentBadge(state, reason);

        Assert.Equal(label, badge.Label);
        Assert.Equal(tone, badge.Tone);
    }
}

