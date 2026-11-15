namespace DigiByte.Wallet.Services;

/// <summary>How the P2P gateway reported the last transaction it relayed (specs/011-p2p-spv-gateway, T088).</summary>
/// <param name="Outcome"><c>accepted</c> or <c>no_response</c> (a rejection throws instead of producing a report).</param>
/// <param name="Rejections">Reasons given by peers that refused it, when any did.</param>
public sealed record BroadcastReport(string Outcome, IReadOnlyList<string> Rejections)
{
    /// <summary>Sent, but no peer has confirmed it yet: the wallet keeps watching.</summary>
    public bool IsUnconfirmedByPeers => Outcome == "no_response";
}

/// <summary>A blockchain source that can say more about a send than its transaction id.</summary>
public interface IBroadcastReporter
{
    BroadcastReport? LastBroadcast { get; }
}
