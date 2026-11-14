using NBitcoin;

namespace DigiByte.Crypto.Headers;

/// <summary>
/// A block the wallet and gateway trust without proof, with the headers before it that the difficulty rule needs.
/// Every header the wallet accepts must chain back to one of these.
/// </summary>
public sealed class Checkpoint
{
    public Checkpoint(int firstHeight, IReadOnlyList<BlockHeader80> headers)
    {
        if (headers.Count == 0)
            throw new ArgumentException("A checkpoint needs at least its own header.", nameof(headers));
        FirstHeight = firstHeight;
        Headers = headers;
    }

    /// <summary>Height of the first header in <see cref="Headers"/>.</summary>
    public int FirstHeight { get; }

    /// <summary>Consecutive headers ending at the checkpoint block, oldest first.</summary>
    public IReadOnlyList<BlockHeader80> Headers { get; }

    public int Height => FirstHeight + Headers.Count - 1;

    public uint256 Hash => Headers[^1].Hash;
}

/// <summary>The built-in checkpoints. Refresh them for each release.</summary>
public static class Checkpoints
{
    private static readonly Lazy<Checkpoint> LazyMainnet = new(BuildMainnet);

    /// <summary>The mainnet checkpoint, after Groestl was switched off and well inside the V4 difficulty era.</summary>
    public static Checkpoint Mainnet => LazyMainnet.Value;

    private static Checkpoint BuildMainnet()
    {
        var headers = MainnetCheckpointData.HeaderHex.Select(h => BlockHeader80.Parse(Convert.FromHexString(h))).ToList();
        var checkpoint = new Checkpoint(MainnetCheckpointData.FirstHeight, headers);
        if (checkpoint.Height != MainnetCheckpointData.Height || checkpoint.Hash.ToString() != MainnetCheckpointData.BlockId)
            throw new InvalidOperationException("The built-in mainnet checkpoint data does not match its own height and block id.");
        for (var i = 1; i < headers.Count; i++)
            if (headers[i].PrevHash != headers[i - 1].Hash)
                throw new InvalidOperationException("The built-in mainnet checkpoint headers do not link.");
        return checkpoint;
    }
}
