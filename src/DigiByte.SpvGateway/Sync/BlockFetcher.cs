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
    private readonly TimeSpan _timeout = perPeerTimeout ?? TimeSpan.FromSeconds(20);

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
            var peer = peers.Sessions
                .Where(s => s.IsConnected && !asked.Contains(s.Endpoint) && (s.Services & PeerRecord.NodeNetwork) != 0)
                .FirstOrDefault();
            if (peer is null)
                return null;
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
                continue; // no answer in time or a broken connection: try another peer
            }
            if (raw is null)
                continue;

            var block = TryParse(raw);
            if (block is not null && block.GetHash() == expectedHash && block.Header.HashMerkleRoot == stored.Header.MerkleRoot && block.CheckMerkleRoot())
                return block;

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
