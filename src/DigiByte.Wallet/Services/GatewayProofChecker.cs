using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>A transaction and the merkle proof a gateway returned for it, exactly as received (nothing here is trusted yet).</summary>
/// <param name="Txid">The transaction id the gateway claims.</param>
/// <param name="Height">The height of the block the gateway claims it is in.</param>
/// <param name="BlockHash">The hash of that block as the gateway claims.</param>
/// <param name="Raw">The serialized transaction as hex.</param>
/// <param name="Index">The position of the transaction in the block.</param>
/// <param name="Branch">The merkle branch, bottom first, as hex hashes.</param>
public sealed record GatewayTransactionProof(string Txid, int Height, string BlockHash, string Raw, int Index, IReadOnlyList<string> Branch);

public enum ProofCheckResult
{
    /// <summary>The transaction is in the block the wallet own header chain has at that height.</summary>
    Valid,

    /// <summary>The raw bytes are not a transaction, or the proof fields are not well formed.</summary>
    Malformed,

    /// <summary>The raw transaction does not hash to the id the gateway named.</summary>
    TxidMismatch,

    /// <summary>The transaction serializes to exactly 64 bytes, which can be confused with two merkle nodes.</summary>
    AmbiguousTransaction,

    /// <summary>The wallet has no verified header at that height (it may simply be behind). Not the gateway fault.</summary>
    HeaderNotVerified,

    /// <summary>The block hash the gateway named is not the hash of the wallet own header at that height.</summary>
    BlockHashMismatch,

    /// <summary>The branch does not lead from the transaction to the merkle root of the wallet own header.</summary>
    ProofRejected,
}

public static class ProofCheckResultExtensions
{
    /// <summary>
    /// Whether the result shows the source sent something false, so it should be reported faulty and the next source used. A header the
    /// wallet has not verified yet says nothing against the source.
    /// </summary>
    public static bool IsSourceFault(this ProofCheckResult result) => result is not (ProofCheckResult.Valid or ProofCheckResult.HeaderNotVerified);
}

/// <summary>
/// Checks a gateway proof against the wallet own verified headers (spec FR-013, SC-001, T068). The header comes from the wallet store,
/// never from the gateway reply: the reply only supplies the transaction and the branch.
/// </summary>
public sealed class GatewayProofChecker(GatewayHeaderStore headers)
{
    public ProofCheckResult Check(GatewayTransactionProof proof)
    {
        Transaction transaction;
        List<uint256> branch;
        uint256 claimedTxid, claimedBlock;
        try
        {
            transaction = Transaction.Parse(proof.Raw, DigiByteNetwork.Mainnet);
            branch = proof.Branch.Select(uint256.Parse).ToList();
            claimedTxid = uint256.Parse(proof.Txid);
            claimedBlock = uint256.Parse(proof.BlockHash);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or EndOfStreamException or IndexOutOfRangeException)
        {
            return ProofCheckResult.Malformed;
        }
        if (transaction.Inputs.Count == 0 || transaction.Outputs.Count == 0 || proof.Index < 0)
            return ProofCheckResult.Malformed;

        var txid = transaction.GetHash();
        if (txid != claimedTxid)
            return ProofCheckResult.TxidMismatch;
        if (MerkleProof.IsAmbiguousTransactionSize(transaction.WithOptions(TransactionOptions.None).ToBytes()))
            return ProofCheckResult.AmbiguousTransaction;

        var header = headers.HeaderAt(proof.Height);
        if (header is null)
            return ProofCheckResult.HeaderNotVerified;
        if (header.Hash != claimedBlock)
            return ProofCheckResult.BlockHashMismatch;

        return MerkleProof.Verify(txid, proof.Index, branch, header.MerkleRoot) ? ProofCheckResult.Valid : ProofCheckResult.ProofRejected;
    }
}
