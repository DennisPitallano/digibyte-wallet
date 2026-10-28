namespace DigiByte.Wallet.Services;

public enum NodeSourceDot
{
    /// <summary>No indicator at all (node use is off).</summary>
    None,
    /// <summary>Own node active.</summary>
    Green,
    /// <summary>Public explorers in use, which is normal, including while the node syncs.</summary>
    Neutral,
    /// <summary>Real degradation: the node was active and the wallet fell back.</summary>
    Amber,
    /// <summary>No data source is reachable.</summary>
    Red,
}

/// <summary>What the status indicator, detail panel and banner should show.</summary>
public sealed record NodeSourceStatusView(
    bool Visible,
    NodeSourceDot Dot,
    string Detail,
    string? PrivacyLine,
    bool ShowBanner,
    string? BannerText);

/// <summary>
/// Turns the monitor state into the wording of specs/005-wallet-own-node-source/contracts/status-ui.md. Pure, so the copy and the
/// banner rule are unit tested. All strings are English and live here so translations can be added later without touching the logic.
/// <para>
/// The banner appears only on real degradation (research R8): the node was active this session and the wallet fell back, or every
/// source failed. A node that is still syncing, or never became ready, is shown in the detail panel only.
/// </para>
/// </summary>
public static class NodeSourceStatus
{
    public const string PrivacyLine =
        "The wallet team's servers, not a third-party explorer, see the public transactions and addresses this wallet sends or looks up through it. Your keys never leave your device.";

    private const string ExplorersPrefix = "Using public block explorers. ";

    public static NodeSourceStatusView Compute(NodeSourceState state, NodeNotReadyReason reason, bool nodeWasActive, bool allSourcesFailed)
    {
        if (state == NodeSourceState.Disabled)
            return new NodeSourceStatusView(false, NodeSourceDot.None, "Using public block explorers.", null, false, null);

        if (allSourcesFailed)
        {
            return new NodeSourceStatusView(
                true, NodeSourceDot.Red,
                "No data source is reachable. Showing your last known balance, which may be out of date.",
                null,
                true,
                "Can't reach the blockchain right now. Showing your last known balance, which may be out of date. Sending is unavailable until the connection returns.");
        }

        if (state == NodeSourceState.Active)
        {
            return new NodeSourceStatusView(
                true, NodeSourceDot.Green,
                "Sending, fees and transaction details use the DigiByte node run by the wallet team. Balances come from public block explorers, with the wallet team's node as a backup.",
                PrivacyLine, false, null);
        }

        var detail = state == NodeSourceState.Stabilizing
            ? ExplorersPrefix + "The wallet team's own node is being checked and will be used automatically when it is ready."
            : reason switch
            {
                NodeNotReadyReason.Starting or NodeNotReadyReason.Syncing =>
                    ExplorersPrefix + "The wallet team's own node is still syncing and will be used automatically when it is ready.",
                NodeNotReadyReason.HeightDrift =>
                    ExplorersPrefix + "The wallet team's own node is out of step with the network and is not being used.",
                _ =>
                    ExplorersPrefix + "The wallet team's own node is not available right now.",
            };

        if (nodeWasActive)
        {
            return new NodeSourceStatusView(
                true, NodeSourceDot.Amber, detail, null, true,
                "The wallet team's node is unavailable. Using public explorers instead.");
        }

        return new NodeSourceStatusView(true, NodeSourceDot.Neutral, detail, null, false, null);
    }
}
