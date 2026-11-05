using DigiByte.Crypto.Psbt;
using NBitcoin;

namespace DigiByte.Wallet.Services;

public sealed record PsbtLoadOutcome(PsbtDocument? Document, ReviewSummary Review);

public sealed record PsbtSignOutcome(SigningResult? Result, ReviewSummary Review, string? Error)
{
    public bool Succeeded => Result is not null;
}

/// <summary>
/// Loads, reviews and signs PSBTs. Gathers the chain facts the analyzer needs and holds no PSBT between calls: the page
/// keeps the document, and every sign repeats the chain checks (FR-008). Never logs PSBT contents.
/// </summary>
public sealed class PsbtSigningService(IBlockchainService chain, IPsbtKeyProvider keys)
{
    public async Task<PsbtLoadOutcome> LoadAsync(string? text) => await ReviewLoadedAsync(PsbtDocument.FromText(text, keys.Network));

    public async Task<PsbtLoadOutcome> LoadAsync(byte[] bytes) => await ReviewLoadedAsync(PsbtDocument.FromBytes(bytes, keys.Network));

    /// <summary>Reviews a loaded document again, reading the chain afresh.</summary>
    public async Task<ReviewSummary> ReviewAsync(PsbtDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var owned = await keys.GetOwnedScriptsAsync();
        var (facts, status) = await GatherFactsAsync(document.Psbt, owned);
        return PsbtAnalyzer.Analyze(document.Psbt, owned, facts, status, keys.CanSign, keys.Network);
    }

    public async Task<PsbtSignOutcome> SignAsync(PsbtDocument document, bool acknowledged)
    {
        var review = await ReviewAsync(document);

        if (!keys.CanSign)
            return new(null, review, "This wallet can't sign: it is locked or watch-only.");
        if (review.ChainStatus == ChainStatus.Unavailable)
            return new(null, review, "The network couldn't be reached to check this wallet's coins. Try again.");
        if (review.Refusal is { } refusal)
            return new(null, review, refusal.Message);
        if (!review.CanSign)
            return new(null, review, "This PSBT can't be signed.");
        if (review.NeedsAcknowledgment && !acknowledged)
            return new(null, review, "Confirm the warnings before signing.");

        var toSign = review.Inputs
            .Where(i => i.Owner == InputOwner.Wallet && i.SignatureState == SignatureState.Unsigned)
            .Select(i => i.Index)
            .ToList();

        var signed = keys.SignInputs(document.Psbt, toSign);
        var completion = PsbtSigner.TryComplete(signed);
        var signedCount = toSign.Count(i => signed.Inputs[i].PartialSigs.Count > 0);
        var remaining = completion.IsComplete
            ? 0
            : signed.Inputs.Count(i => !i.IsFinalized() && i.PartialSigs.Count == 0);

        return new(new SigningResult(signed.ToBase64(), signedCount, remaining, completion.IsComplete, completion.Transaction), review, null);
    }

    private async Task<PsbtLoadOutcome> ReviewLoadedAsync(PsbtLoadResult loaded)
    {
        if (loaded.Document is not { } document)
            return new(null, ReviewSummary.Refused(loaded.Refusal!));
        return new(document, await ReviewAsync(document));
    }

    /// <summary>
    /// Chain facts per input. This wallet's inputs are proven with the verified unspent list, which throws rather than
    /// returning an empty list when no source answers. Inputs with no PSBT data are looked up by transaction. Other
    /// participants' inputs with PSBT data are not looked up until shared transactions are checked (Story 4).
    /// </summary>
    private async Task<(Dictionary<OutPoint, ChainInputFact> Facts, ChainStatus Status)> GatherFactsAsync(
        PSBT psbt, IReadOnlyDictionary<Script, string> owned)
    {
        var facts = new Dictionary<OutPoint, ChainInputFact>();
        var status = ChainStatus.Verified;
        var unspentByAddress = new Dictionary<string, List<UtxoInfo>?>();

        async Task<List<UtxoInfo>?> UnspentAsync(string address)
        {
            if (unspentByAddress.TryGetValue(address, out var cached)) return cached;
            List<UtxoInfo>? list;
            try
            {
                list = await chain.GetVerifiedUtxosAsync(address);
            }
            catch (Exception)
            {
                list = null;
            }
            unspentByAddress[address] = list;
            return list;
        }

        foreach (var input in psbt.Inputs)
        {
            var outpoint = input.PrevOut;
            var script = StatedScript(input);
            long? amount = null;

            if (script is null)
            {
                var looked = await LookUpTransactionOutputAsync(outpoint);
                if (looked.Lookup != ChainLookup.Found)
                {
                    facts[outpoint] = looked;
                    continue;
                }
                script = looked.Script;
                amount = looked.Amount;
                facts[outpoint] = looked;
            }

            if (script is null || !owned.TryGetValue(script, out var address)) continue;

            var unspent = await UnspentAsync(address);
            if (unspent is null)
            {
                status = ChainStatus.Unavailable;
                facts[outpoint] = new ChainInputFact(outpoint, ChainLookup.Unavailable, amount, script);
                continue;
            }

            var match = unspent.FirstOrDefault(u =>
                u.OutputIndex == outpoint.N && string.Equals(u.TxId, outpoint.Hash.ToString(), StringComparison.OrdinalIgnoreCase));
            facts[outpoint] = match is null
                ? new ChainInputFact(outpoint, ChainLookup.NotFound, amount, script, IsUnspent: false)
                : new ChainInputFact(
                    outpoint,
                    ChainLookup.Found,
                    match.AmountSatoshis,
                    string.IsNullOrEmpty(match.ScriptPubKey) ? script : Script.FromHex(match.ScriptPubKey),
                    IsUnspent: true,
                    Confirmations: match.Confirmations);
        }

        return (facts, status);
    }

    private async Task<ChainInputFact> LookUpTransactionOutputAsync(OutPoint outpoint)
    {
        TransactionInfo? tx;
        try
        {
            tx = await chain.GetTransactionAsync(outpoint.Hash.ToString());
        }
        catch (Exception)
        {
            return new ChainInputFact(outpoint, ChainLookup.Unavailable);
        }

        var output = tx?.Outputs.FirstOrDefault(o => o.Index == outpoint.N);
        if (output is null) return new ChainInputFact(outpoint, ChainLookup.NotFound);

        var script = ScriptOf(output);
        return script is null
            ? new ChainInputFact(outpoint, ChainLookup.NotFound)
            : new ChainInputFact(outpoint, ChainLookup.Found, output.AmountSatoshis, script);
    }

    private Script? ScriptOf(TxOutput output)
    {
        try
        {
            if (!string.IsNullOrEmpty(output.ScriptHex)) return Script.FromHex(output.ScriptHex);
            if (!string.IsNullOrEmpty(output.Address)) return BitcoinAddress.Create(output.Address, keys.Network).ScriptPubKey;
        }
        catch (Exception)
        {
            // An unreadable script or an address for another network identifies nothing.
        }
        return null;
    }

    private static Script? StatedScript(PSBTInput input)
    {
        if (input.WitnessUtxo is { } witnessUtxo) return witnessUtxo.ScriptPubKey;
        if (input.NonWitnessUtxo is { } previous && input.PrevOut.N < previous.Outputs.Count)
            return previous.Outputs[(int)input.PrevOut.N].ScriptPubKey;
        return null;
    }
}
