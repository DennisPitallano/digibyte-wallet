using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Peers;
using NBitcoin;

namespace DigiByte.SpvGateway.Sync;

/// <summary>What one download round achieved.</summary>
/// <param name="Accepted">Headers newly stored.</param>
/// <param name="PeersDropped">Peers disconnected and banned for sending headers that failed the checks.</param>
/// <param name="TipHeight">The best chain height afterwards.</param>
/// <param name="Reached">True when a peer had nothing newer to send, so the gateway is level with it.</param>
public sealed record DownloadResult(int Accepted, int PeersDropped, int TipHeight, bool Reached);

/// <summary>
/// Fetches headers from the connected peers and hands them to <see cref="HeaderSync"/> (spec FR-002, T033). Each request names the
/// best chain's recent hashes, so the peer answers from the point where the two chains part, which is how a reorganization is found.
/// A peer whose headers fail the checks is dropped and the next peer is asked.
/// </summary>
public sealed class HeaderDownloader(HeaderStore store, HeaderSync sync, PeerManager peers, TimeSpan? requestTimeout = null)
{
    private readonly TimeSpan _timeout = requestTimeout ?? TimeSpan.FromSeconds(15);
    private const int MaxRounds = 5000;
    private int _rotation;

    public async Task<DownloadResult> DownloadAsync(CancellationToken cancellationToken)
    {
        var accepted = 0;
        var dropped = 0;
        var reached = false;
        var failedPeers = new HashSet<PeerEndpoint>();

        for (var round = 0; round < MaxRounds && !cancellationToken.IsCancellationRequested; round++)
        {
            var peer = NextPeer(failedPeers);
            if (peer is null)
                break;

            IReadOnlyList<BlockHeader80> batch;
            try
            {
                // A peer that never answers must not hold the whole round: each request has its own time limit.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_timeout);
                batch = await peer.GetHeadersAsync(Locator(), timeout.Token);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failedPeers.Add(peer.Endpoint);
                continue;
            }

            if (batch.Count == 0)
            {
                reached = true;
                break;
            }

            var result = sync.Ingest(batch);
            accepted += result.Accepted;
            if (result.Rejected is not null)
            {
                await peers.ReportInvalidHeaderAsync(peer);
                failedPeers.Add(peer.Endpoint);
                dropped++;
            }
            else if (result.Accepted == 0 && result.Duplicates == batch.Count)
            {
                // A peer repeating what the gateway already has is level with it.
                reached = true;
                break;
            }
        }
        return new DownloadResult(accepted, dropped, sync.TipHeight, reached);
    }

    private IPeerSession? NextPeer(HashSet<PeerEndpoint> failed)
    {
        var usable = peers.Sessions.Where(s => s.IsConnected && !failed.Contains(s.Endpoint)).ToList();
        return usable.Count == 0 ? null : usable[Math.Abs(_rotation++) % usable.Count];
    }

    /// <summary>The newest ten best-chain hashes, then every second, fourth, eighth... one back to the earliest stored header.</summary>
    private List<uint256> Locator()
    {
        var locator = new List<uint256>();
        var tip = store.BestTip();
        if (tip is null)
            return locator;
        var earliest = store.EarliestHeight() ?? tip.Height;
        var height = tip.Height;
        var step = 1;
        while (height >= earliest)
        {
            if (store.GetBest(height) is not { } stored)
                break;
            locator.Add(stored.Header.Hash);
            if (locator.Count >= 10)
                step *= 2;
            height -= step;
        }
        if (store.GetBest(earliest) is { } first && !locator.Contains(first.Header.Hash))
            locator.Add(first.Header.Hash);
        return locator;
    }
}
