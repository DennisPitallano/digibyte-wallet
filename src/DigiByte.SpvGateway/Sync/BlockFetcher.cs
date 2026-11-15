using DigiByte.Crypto.Networks;
using DigiByte.SpvGateway.Peers;
using NBitcoin;

namespace DigiByte.SpvGateway.Sync;

/// <summary>
/// Gets whole blocks from full-history peers (spec FR-003, T061). A block is used only if it is the block the stored header names
/// (same hash) and its transactions hash to that header merkle root. A peer that sends anything else is dropped and banned, and the
/// next peer is asked; a peer that simply does not have the block, or does not answer in time, is skipped.
/// </summary>
public sealed class BlockFetcher(PeerManager peers, HeaderStore headers, Network network, TimeSpan? perPeerTimeout = null)
{
    private readonly TimeSpan _timeout = perPeerTimeout ?? TimeSpan.FromSeconds(10);
    private int _rotation;

    // How well each peer has served blocks lately. A peer that times out or says it does not have a block (a pruned node that still
    // advertises full history) is asked last, and one that keeps failing is left alone for a while, so the fill does not wait on it.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<PeerEndpoint, (int Score, DateTime Since)> _scores = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
    private const int GiveUpBelow = -6;

    private int ScoreOf(PeerEndpoint endpoint)
    {
        if (!_scores.TryGetValue(endpoint, out var entry))
            return 0;
        if (DateTime.UtcNow - entry.Since > Cooldown)
        {
            _scores.TryRemove(endpoint, out _);
            return 0; // forgiven: try it again
        }
        return entry.Score;
    }

    private void Reward(PeerEndpoint endpoint) => _scores.AddOrUpdate(endpoint, (1, DateTime.UtcNow), (_, e) => (Math.Min(e.Score + 1, 10), DateTime.UtcNow));

    private void Penalize(PeerEndpoint endpoint) => _scores.AddOrUpdate(endpoint, (-2, DateTime.UtcNow), (_, e) => (e.Score - 2, DateTime.UtcNow));

    /// <summary>The best-chain block at <paramref name="height"/>, or null when no peer could supply a valid one.</summary>
    public async Task<Block?> FetchAsync(int height, CancellationToken cancellationToken)
    {
        var stored = headers.GetBest(height);
        if (stored is null)
            return null;
        var expectedHash = stored.Header.Hash;

        var asked = new HashSet<PeerEndpoint>();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            // Spread the requests over the full-history peers, so many blocks can be on the way at once instead of all asking the same peer.
            var candidates = peers.Sessions
                .Where(s => s.IsConnected && !asked.Contains(s.Endpoint) && (s.Services & PeerRecord.NodeNetwork) != 0)
                .Select(s => (Session: s, Score: ScoreOf(s.Endpoint)))
                .ToList();
            // Peers that keep failing are left out while others remain; when only failing peers are left they are still tried.
            var usable = candidates.Where(c => c.Score > GiveUpBelow).ToList();
            if (usable.Count == 0)
                usable = candidates;
            if (usable.Count == 0)
                return null;
            // The best-scoring half takes the requests in turn.
            var best = usable.OrderByDescending(c => c.Score).Take(Math.Max(1, (usable.Count + 1) / 2)).ToList();
            var peer = best[(int)((uint)Interlocked.Increment(ref _rotation) % (uint)best.Count)].Session;
            asked.Add(peer.Endpoint);

            byte[]? raw;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_timeout);
                raw = await peer.GetBlockAsync(expectedHash, timeout.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Penalize(peer.Endpoint);
                continue; // no answer in time or a broken connection: try another peer
            }
            if (raw is null)
            {
                Penalize(peer.Endpoint);
                continue;
            }

            var block = TryParse(raw);
            if (block is not null && block.GetHash() == expectedHash && block.Header.HashMerkleRoot == stored.Header.MerkleRoot && block.CheckMerkleRoot())
            {
                Reward(peer.Endpoint);
                return block;
            }

            await peers.ReportInvalidBlockAsync(peer);
        }
        return null;
    }

    private Block? TryParse(byte[] raw)
    {
        try
        {
            return Block.Load(raw, network);
        }
        catch (Exception e) when (e is FormatException or EndOfStreamException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
