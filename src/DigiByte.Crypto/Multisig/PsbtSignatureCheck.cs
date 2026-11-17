using NBitcoin;

namespace DigiByte.Crypto.Multisig;

/// <summary>One partial signature in a PSBT and whether it really signs its input.</summary>
public sealed record PartialSignatureCheck(int InputIndex, string PublicKeyHex, bool IsValid);

/// <summary>
/// Checks partial signatures in a multisig PSBT against each input's signature hash, and checks which multisig a PSBT
/// spends. Used by the online signing room (specs/017-multisig-online-signing, research R6): neither the room server nor
/// a co-signer's wallet counts a signature it has not verified. Never throws for malformed signatures; they are reported
/// invalid.
/// </summary>
public static class PsbtSignatureCheck
{
    /// <summary>Every partial signature in <paramref name="psbt"/>, with whether it verifies for its input.</summary>
    public static IReadOnlyList<PartialSignatureCheck> Check(PSBT psbt)
    {
        ArgumentNullException.ThrowIfNull(psbt);
        var results = new List<PartialSignatureCheck>();
        var tx = psbt.GetGlobalTransaction();
        var precomputed = (PrecomputedTransactionData?)null;

        for (var i = 0; i < psbt.Inputs.Count; i++)
        {
            var input = psbt.Inputs[i];
            ICoin? coin = null;
            try { coin = input.GetSignableCoin(); } catch { /* missing spent output or script */ }

            foreach (var (pubKey, sig) in input.PartialSigs)
            {
                var valid = false;
                if (coin is not null)
                {
                    try
                    {
                        precomputed ??= TryPrecompute(psbt, tx);
                        var hash = tx.GetSignatureHash(coin.GetScriptCode(), i, sig.SigHash, coin.TxOut, coin.GetHashVersion(), precomputed);
                        valid = pubKey.Verify(hash, sig.Signature);
                    }
                    catch { valid = false; }
                }
                results.Add(new PartialSignatureCheck(i, pubKey.ToHex(), valid));
            }
        }
        return results;
    }

    /// <summary>Public keys (hex, lower case) that have a valid signature on every input.</summary>
    public static IReadOnlyList<string> KeysValidOnEveryInput(PSBT psbt)
    {
        var checks = Check(psbt);
        if (psbt.Inputs.Count == 0) return [];
        return checks.Where(c => c.IsValid)
            .GroupBy(c => c.PublicKeyHex, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(c => c.InputIndex).Distinct().Count() == psbt.Inputs.Count)
            .Select(g => g.Key.ToLowerInvariant())
            .ToList();
    }

    /// <summary>
    /// True when every input spends the M-of-N multisig made from <paramref name="coSignerKeysHex"/> (in any order, sorted
    /// as BIP67 requires), either as P2WSH or as P2SH-P2WSH. An input without its spent output (witness UTXO) fails.
    /// </summary>
    public static bool SpendsMultisig(PSBT psbt, IReadOnlyList<string> coSignerKeysHex, int requiredSignatures)
    {
        Script redeem;
        try
        {
            var keys = coSignerKeysHex.Select(h => new PubKey(h)).ToArray();
            if (keys.Length == 0 || requiredSignatures < 1 || requiredSignatures > keys.Length) return false;
            redeem = new MultisigService().CreateRedeemScript(requiredSignatures, keys);
        }
        catch { return false; }

        var p2wsh = redeem.WitHash.ScriptPubKey;
        var p2shP2wsh = p2wsh.Hash.ScriptPubKey;
        if (psbt.Inputs.Count == 0) return false;

        foreach (var input in psbt.Inputs)
        {
            var spent = input.WitnessUtxo?.ScriptPubKey;
            if (spent is null) return false;
            if (spent != p2wsh && spent != p2shP2wsh) return false;
        }
        return true;
    }

    private static PrecomputedTransactionData? TryPrecompute(PSBT psbt, Transaction tx)
    {
        try
        {
            var spent = psbt.Inputs.Select(i => i.WitnessUtxo ?? i.GetTxOut()).ToArray();
            return spent.Any(o => o is null) ? null : tx.PrecomputeTransactionData(spent!);
        }
        catch { return null; }
    }
}
