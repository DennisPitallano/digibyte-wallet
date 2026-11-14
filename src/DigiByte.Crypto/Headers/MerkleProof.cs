using DigiByte.Crypto.Networks;
using NBitcoin;
using NBitcoin.Crypto;

namespace DigiByte.Crypto.Headers;

/// <summary>
/// Checks that a transaction is in a block, from the merkle branch a gateway supplies and the block header's merkle root.
/// Bitcoin-style tree: each pair of hashes is joined and double SHA-256 hashed, and a lone node is paired with itself.
/// </summary>
public static class MerkleProof
{
    /// <summary>No real block has a tree this deep, and a longer branch is never needed.</summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// Returns true when <paramref name="txid"/> at position <paramref name="index"/> hashes up through <paramref name="branch"/>
    /// to <paramref name="merkleRoot"/>. The index must fit the branch (its bits beyond the branch length are zero).
    /// </summary>
    public static bool Verify(uint256 txid, int index, IReadOnlyList<uint256> branch, uint256 merkleRoot)
    {
        if (index < 0 || branch.Count > MaxDepth)
            return false;
        if (branch.Count < 31 && (index >> branch.Count) != 0)
            return false;

        var current = txid;
        var position = index;
        var buffer = new byte[64];
        foreach (var sibling in branch)
        {
            if ((position & 1) == 1)
            {
                sibling.ToBytes().CopyTo(buffer, 0);
                current.ToBytes().CopyTo(buffer, 32);
            }
            else
            {
                current.ToBytes().CopyTo(buffer, 0);
                sibling.ToBytes().CopyTo(buffer, 32);
            }
            current = Hashes.DoubleSHA256(buffer);
            position >>= 1;
        }
        return current == merkleRoot;
    }

    /// <summary>The transaction id (double SHA-256 of the serialization without witness data) of a raw transaction.</summary>
    public static uint256 TransactionId(byte[] rawTransaction)
    {
        var tx = Transaction.Load(rawTransaction, DigiByteNetwork.Mainnet);
        return tx.GetHash();
    }

    /// <summary>
    /// A transaction whose non-witness serialization is exactly 64 bytes can be confused with two merkle nodes joined together,
    /// so callers must refuse it. Pass the non-witness serialization.
    /// </summary>
    public static bool IsAmbiguousTransactionSize(ReadOnlySpan<byte> nonWitnessSerialization) => nonWitnessSerialization.Length == 64;
}
