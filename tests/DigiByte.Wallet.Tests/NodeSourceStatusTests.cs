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
        Assert.Equal(NodeBanner.NodeFallback, v.Banner);
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
        Assert.Equal(NodeBanner.None, v.Banner);
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
        Assert.Equal(NodeBanner.Unreachable, v.Banner);
        Assert.Contains("No data source is reachable", v.Detail);
    }

    [Fact]
    public void AllSourcesFailed_TakesPriorityOverTheNodeFallbackBanner()
    {
        var v = View(NodeSourceState.NotReady, NodeNotReadyReason.Syncing, wasActive: true, allFailed: true);

        Assert.DoesNotContain("wallet team's node is unavailable", v.BannerText);
        Assert.Equal(NodeBanner.Unreachable, v.Banner);
    }

    [Fact]
    public void Healthy_NoBannerAndNoWarning()
    {
        var v = View(NodeSourceState.Active, wasActive: true, allFailed: false);

        Assert.False(v.ShowBanner);
        Assert.Null(v.BannerText);
        Assert.Equal(NodeBanner.None, v.Banner);
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


/// <summary>The light-client gateway in the status wording (specs/011-p2p-spv-gateway, T101, T110).</summary>
public class GatewayStatusWordingTests
{
    private static NodeSourceStatusView NodeOff() =>
        NodeSourceStatus.Compute(NodeSourceState.Disabled, NodeNotReadyReason.None, nodeWasActive: false, allSourcesFailed: false);

    [Fact]
    public void Without_a_gateway_the_view_is_returned_unchanged()
    {
        var view = NodeOff();

        Assert.Equal(view, NodeSourceStatus.WithGateway(view, NodeSourceState.Disabled, NodeNotReadyReason.None, gatewayAnswering: false));
    }

    [Fact]
    public void A_gateway_that_is_answering_is_named_with_the_address_note_and_never_as_protection_from_a_dishonest_source()
    {
        var view = NodeSourceStatus.WithGateway(NodeOff(), NodeSourceState.Active, NodeNotReadyReason.None, gatewayAnswering: true);

        Assert.True(view.Visible);
        Assert.Equal(NodeSourceDot.Green, view.Dot);
        Assert.Contains("light-client gateway", view.Detail);
        Assert.Contains("sees the addresses this wallet asks about", view.Detail);
        Assert.Contains("checked itself", view.Detail);
        Assert.DoesNotContain("protect", view.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trustless", view.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_ready_gateway_that_is_not_answering_is_a_backup_and_the_dot_stays_neutral()
    {
        var view = NodeSourceStatus.WithGateway(NodeOff(), NodeSourceState.Active, NodeNotReadyReason.None, gatewayAnswering: false);

        Assert.Equal(NodeSourceDot.Neutral, view.Dot);
        Assert.Contains("ready as a backup", view.Detail);
        Assert.Contains("sees the addresses", view.Detail);
    }

    [Theory]
    [InlineData(NodeSourceState.Stabilizing, NodeNotReadyReason.None, "being checked")]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Syncing, "still syncing")]
    [InlineData(NodeSourceState.NotReady, NodeNotReadyReason.Starting, "still syncing")]
    [InlineData(NodeSourceState.Unavailable, NodeNotReadyReason.None, "not available")]
    public void A_gateway_that_is_not_ready_says_why_and_makes_no_privacy_claim_about_itself(NodeSourceState state, NodeNotReadyReason reason, string expected)
    {
        var view = NodeSourceStatus.WithGateway(NodeOff(), state, reason, gatewayAnswering: false);

        Assert.Contains(expected, view.Detail);
        Assert.DoesNotContain("sees the addresses", view.Detail);
        Assert.Equal(NodeSourceDot.Neutral, view.Dot);
    }

    [Fact]
    public void The_node_wording_is_kept_and_the_gateway_line_is_added_after_it()
    {
        var node = NodeSourceStatus.Compute(NodeSourceState.Active, NodeNotReadyReason.None, nodeWasActive: true, allSourcesFailed: false);

        var view = NodeSourceStatus.WithGateway(node, NodeSourceState.Active, NodeNotReadyReason.None, gatewayAnswering: false);

        Assert.StartsWith(node.Detail, view.Detail);
        Assert.Equal(node.PrivacyLine, view.PrivacyLine);
        Assert.Equal(node.Dot, view.Dot);
    }

    [Fact]
    public void A_total_outage_keeps_its_red_dot_and_banner_whatever_the_gateway_says()
    {
        var outage = NodeSourceStatus.Compute(NodeSourceState.Active, NodeNotReadyReason.None, nodeWasActive: true, allSourcesFailed: true);

        var view = NodeSourceStatus.WithGateway(outage, NodeSourceState.Active, NodeNotReadyReason.None, gatewayAnswering: false);

        Assert.Equal(NodeSourceDot.Red, view.Dot);
        Assert.True(view.ShowBanner);
    }

    [Theory]
    [InlineData(NodeSourceState.Disabled, "Not in use")]
    [InlineData(NodeSourceState.Active, "Ready")]
    [InlineData(NodeSourceState.Stabilizing, "Checking")]
    [InlineData(NodeSourceState.Unavailable, "Unreachable")]
    public void The_deployment_badge_follows_the_monitor_state(NodeSourceState state, string label)
    {
        Assert.Equal(label, NodeSourceStatus.GatewayBadge(state, NodeNotReadyReason.None).Label);
    }
}
