using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Multisig;

/// <summary>
/// Core multisig operations: redeem script generation, address derivation, PSBT workflow.
/// Supports M-of-N P2SH-P2WSH multisig.
/// </summary>
public class MultisigService
{
    private readonly Network _network;

    public MultisigService(Network? network = null)
    {
        _network = network ?? DigiByteNetwork.Mainnet;
    }

    /// <summary>
    /// Creates an M-of-N multisig redeem script from the given public keys.
    /// Keys are sorted lexicographically (BIP67) for deterministic script generation.
    /// </summary>
    public Script CreateRedeemScript(int requiredSignatures, IEnumerable<PubKey> pubKeys)
    {
        var sortedKeys = pubKeys.OrderBy(k => k.ToHex()).ToArray();

        if (requiredSignatures < 1 || requiredSignatures > sortedKeys.Length)
            throw new ArgumentException($"Required signatures must be between 1 and {sortedKeys.Length}.");

        if (sortedKeys.Length > 15)
            throw new ArgumentException("Maximum 15 public keys allowed in a multisig script.");

        return PayToMultiSigTemplate.Instance.GenerateScriptPubKey(requiredSignatures, sortedKeys);
    }

    /// <summary>
    /// Derives a P2SH-P2WSH address from a multisig redeem script.
    /// This is the most compatible multisig address format (starts with 'S' on DigiByte mainnet).
    /// </summary>
    public BitcoinAddress GetP2SH_P2WSH_Address(Script redeemScript)
    {
        // Wrap redeem script in witness script hash, then in script hash
        var witnessScript = redeemScript.WitHash.ScriptPubKey;
        return witnessScript.Hash.GetAddress(_network);
    }

    /// <summary>
    /// Derives a native P2WSH address from a multisig redeem script.
    /// More efficient (lower fees) but requires all participants support SegWit.
    /// </summary>
    public BitcoinAddress GetP2WSH_Address(Script redeemScript)
    {
        return redeemScript.WitHash.GetAddress(_network);
    }

    /// <summary>
    /// Creates a PSBT (Partially Signed Bitcoin Transaction) for a multisig spend.
    /// The PSBT can be shared with co-signers for collecting signatures.
    /// </summary>
    public PSBT CreateMultisigPSBT(
        Script redeemScript,
        IEnumerable<Coin> coins,
        BitcoinAddress destination,
        Money amount,
        BitcoinAddress changeAddress,
        FeeRate feeRate,
        string? memo = null)
    {
        var builder = _network.CreateTransactionBuilder();
        builder.DustPrevention = false;

        // Add coins with the redeem script for proper signing
        var scriptCoins = coins.Select(c => c.ToScriptCoin(redeemScript)).ToArray();
        builder.AddCoins(scriptCoins);

        builder.Send(destination, amount);

        if (!string.IsNullOrWhiteSpace(memo))
        {
            var memoBytes = System.Text.Encoding.UTF8.GetBytes(memo);
            if (memoBytes.Length > 80)
                memoBytes = memoBytes[..80];
            var opReturnScript = TxNullDataTemplate.Instance.GenerateScriptPubKey(memoBytes);
            builder.Send(opReturnScript, Money.Zero);
        }

        builder.SetChange(changeAddress);
        builder.SendEstimatedFees(feeRate);

        return builder.BuildPSBT(sign: false);
    }

    /// <summary>
    /// Signs a PSBT with a single key. Call this for each co-signer.
    /// </summary>
    public PSBT SignPSBT(PSBT psbt, Key privateKey)
    {
        psbt.SignWithKeys(privateKey);
        return psbt;
    }

    /// <summary>
    /// Combines multiple partially-signed PSBTs into one.
    /// Each PSBT should contain signatures from different co-signers.
    /// </summary>
    public PSBT CombinePSBTs(IEnumerable<PSBT> psbts)
    {
        var list = psbts.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one PSBT is required.");

        var combined = list[0];
        for (int i = 1; i < list.Count; i++)
        {
            combined = combined.Combine(list[i]);
        }

        return combined;
    }

    /// <summary>
    /// Counts how many valid signatures are present in a PSBT input.
    /// </summary>
    public int CountSignatures(PSBT psbt, int inputIndex)
    {
        if (inputIndex < 0 || inputIndex >= psbt.Inputs.Count)
            return 0;

        var input = psbt.Inputs[inputIndex];
        return input.PartialSigs.Count;
    }

    /// <summary>
    /// Checks if a PSBT has enough signatures to be finalized.
    /// </summary>
    public bool CanFinalize(PSBT psbt, int requiredSignatures)
    {
        return psbt.Inputs.All(input => input.PartialSigs.Count >= requiredSignatures);
    }

    /// <summary>
    /// Finalizes a fully-signed PSBT and extracts the broadcast-ready transaction.
    /// </summary>
    public Transaction FinalizePSBT(PSBT psbt)
    {
        psbt.Finalize();
        return psbt.ExtractTransaction();
    }

    /// <summary>
    /// Attempts to finalize and extract. Returns null if not enough signatures.
    /// </summary>
    public Transaction? TryFinalize(PSBT psbt)
    {
        try
        {
            psbt.Finalize();
            return psbt.ExtractTransaction();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes a PSBT to base64 for sharing between co-signers.
    /// </summary>
    public static string SerializePSBT(PSBT psbt) => psbt.ToBase64();

    /// <summary>
    /// Deserializes a PSBT from base64.
    /// </summary>
    public PSBT DeserializePSBT(string base64)
    {
        return PSBT.Parse(base64, _network);
    }

    /// <summary>
    /// Extracts public keys that have signed a PSBT input.
    /// </summary>
    public IReadOnlyList<PubKey> GetSigners(PSBT psbt, int inputIndex)
    {
        if (inputIndex < 0 || inputIndex >= psbt.Inputs.Count)
            return [];

        return psbt.Inputs[inputIndex].PartialSigs.Keys.ToList();
    }
}
