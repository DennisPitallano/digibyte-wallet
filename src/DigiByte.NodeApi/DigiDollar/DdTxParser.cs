using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.NodeApi.DigiDollar;

/// <summary>One DigiDollar output: a value-0 canonical P2TR output paired with its amount from the DD OP_RETURN.</summary>
public sealed record DdParsedOutput(int Vout, byte[] OutputKey, long AmountCents, string ScriptPubKeyHex);

/// <summary>An outpoint a DD transaction spends.</summary>
public readonly record struct DdOutpointRef(string Txid, int Vout);

/// <summary>A well-formed DigiDollar transaction, as seen by the index.</summary>
public sealed record DdParsedTx(
    string Txid,
    DigiDollarTxType Type,
    IReadOnlyList<DdParsedOutput> Outputs,
    IReadOnlyList<DdOutpointRef> Spends);

/// <summary>
/// Extracts DigiDollar outputs from a transaction using the rules DigiByte Core validates
/// (research R1): DD marker in nVersion, value-0 canonical P2TR outputs, exactly one DD OP_RETURN whose
/// amount count equals the DD output count and whose type matches the version type.
/// Returns null for anything that is not a well-formed DD transaction.
/// </summary>
public static class DdTxParser
{
    public static DdParsedTx? Parse(Transaction tx)
    {
        if (!DigiDollarScripts.IsDigiDollarVersion(tx.Version)) return null;

        // Exactly one DD OP_RETURN.
        List<long>? amounts = null;
        var opReturnType = DigiDollarTxType.None;
        foreach (var o in tx.Outputs)
        {
            List<long> a;
            DigiDollarTxType t;
            if (DigiDollarScripts.TryParseOpReturn(o.ScriptPubKey, out t, out a)) { }
            else if (DigiDollarScripts.TryParseMintOpReturn(o.ScriptPubKey, out var mintCents)) { t = DigiDollarTxType.Mint; a = [mintCents]; }
            else continue;
            if (amounts is not null) return null; // more than one DD OP_RETURN
            amounts = a;
            opReturnType = t;
        }
        if (amounts is null) return null;
        if (opReturnType != DigiDollarScripts.GetTxType(tx.Version)) return null;

        var outputs = new List<DdParsedOutput>();
        for (var i = 0; i < tx.Outputs.Count; i++)
        {
            var o = tx.Outputs[i];
            if (o.Value != Money.Zero) continue;
            if (!DigiDollarScripts.TryGetOutputKey(o.ScriptPubKey, out var key)) continue;
            // A mint has exactly one DD output whose amount comes first; transfers and redeems pair one amount per DD output.
            var limit = opReturnType == DigiDollarTxType.Mint ? 1 : amounts.Count;
            if (outputs.Count >= limit) return null; // more DD outputs than amounts
            outputs.Add(new DdParsedOutput(i, key, amounts[outputs.Count], o.ScriptPubKey.ToHex()));
        }
        if (outputs.Count != (opReturnType == DigiDollarTxType.Mint ? 1 : amounts.Count)) return null;

        var spends = tx.Inputs
            .Where(i => !i.PrevOut.IsNull)
            .Select(i => new DdOutpointRef(i.PrevOut.Hash.ToString(), (int)i.PrevOut.N))
            .ToList();

        return new DdParsedTx(tx.GetHash().ToString(), opReturnType, outputs, spends);
    }
}
