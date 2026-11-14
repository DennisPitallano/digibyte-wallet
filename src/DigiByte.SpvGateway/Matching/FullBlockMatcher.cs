using DigiByte.SpvGateway.Index;

namespace DigiByte.SpvGateway.Matching;

/// <summary>A transaction that matched: the block height and its position in the block.</summary>
public readonly record struct MatchHit(int Height, int TxIndex);

/// <summary>Answers "which transactions involve these scripts or spend these outpoints in this height range". The seam for peer or own-node filters later (M3).</summary>
public interface IBlockMatcher
{
    IReadOnlyList<MatchHit> Match(IReadOnlyCollection<byte[]> scriptKeys, IReadOnlyCollection<byte[]> outpointKeys, int fromHeight, int toHeight);
}

/// <summary>Matches from the gateway own index of whole blocks. Spends of outpoints older than the scan window are found by the outpoint keys.</summary>
public sealed class FullBlockMatcher(BlockIndexStore index) : IBlockMatcher
{
    public IReadOnlyList<MatchHit> Match(IReadOnlyCollection<byte[]> scriptKeys, IReadOnlyCollection<byte[]> outpointKeys, int fromHeight, int toHeight) =>
        index.Find(scriptKeys, outpointKeys, fromHeight, toHeight).Select(h => new MatchHit(h.Height, h.TxIndex)).ToList();
}
