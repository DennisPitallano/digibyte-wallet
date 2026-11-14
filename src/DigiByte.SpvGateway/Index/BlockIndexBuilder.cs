using System.Security.Cryptography;
using NBitcoin;

namespace DigiByte.SpvGateway.Index;

/// <summary>The short hash of an output script (first 8 bytes of SHA-256). A collision only returns an extra transaction, which the wallet then ignores.</summary>
public static class ScriptKey
{
    public const int Length = 8;

    public static byte[] Of(Script script) => SHA256.HashData(script.ToBytes())[..Length];
}

/// <summary>The short hash of an outpoint: first 8 bytes of SHA-256 over the transaction id and the output number.</summary>
public static class OutpointKey
{
    public static byte[] Of(OutPoint outpoint)
    {
        var data = new byte[36];
        outpoint.Hash.ToBytes().CopyTo(data, 0);
        BitConverter.GetBytes(outpoint.N).CopyTo(data, 32);
        return SHA256.HashData(data)[..ScriptKey.Length];
    }
}

/// <summary>The short key of a transaction id (its first 8 bytes), for finding a transaction by id.</summary>
public static class TxidKey
{
    public static byte[] Of(uint256 txid) => txid.ToBytes()[..ScriptKey.Length];
}

/// <summary>A key and the position, within the block, of the transaction it came from.</summary>
public sealed record IndexedKey(int TxIndex, byte[] Key);

/// <summary>What the index keeps for one block (data-model: BlockIndexRecord). Nothing in it relates to any wallet.</summary>
public sealed record BlockIndexEntry(int Height, uint256 Hash, int TransactionCount, IReadOnlyList<IndexedKey> Outputs, IReadOnlyList<IndexedKey> Spends, IReadOnlyList<IndexedKey>? TxKeys = null);

public static class BlockIndexBuilder
{
    /// <summary>Records the short hash of every output script and of every outpoint spent (the coinbase spends nothing).</summary>
    public static BlockIndexEntry Build(Block block, int height)
    {
        var outputs = new List<IndexedKey>();
        var spends = new List<IndexedKey>();
        var txKeys = new List<IndexedKey>();
        for (var i = 0; i < block.Transactions.Count; i++)
        {
            var tx = block.Transactions[i];
            txKeys.Add(new IndexedKey(i, TxidKey.Of(tx.GetHash())));
            foreach (var output in tx.Outputs)
                outputs.Add(new IndexedKey(i, ScriptKey.Of(output.ScriptPubKey)));
            if (tx.IsCoinBase)
                continue;
            foreach (var input in tx.Inputs)
                spends.Add(new IndexedKey(i, OutpointKey.Of(input.PrevOut)));
        }
        return new BlockIndexEntry(height, block.GetHash(), block.Transactions.Count, outputs, spends, txKeys);
    }
}
