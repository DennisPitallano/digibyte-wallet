using System.Text;
using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.Crypto.Psbt;

/// <summary>
/// Decides what a PSBT does and whether it is safe to sign. Pure: no I/O, so every chain fact is supplied by the caller.
/// An input is this wallet's only when its script is one of <c>ownedScripts</c>; key paths in the PSBT are never trusted.
/// Refusals are checked in the order of <see cref="PsbtRefusalReason"/>, and the first that applies wins.
/// </summary>
public static class PsbtAnalyzer
{
    /// <summary>A fee above this share of what leaves the wallet (or of all inputs, when nothing leaves) needs acknowledgment.</summary>
    private const int HighFeePercent = 10;

    public static ReviewSummary Analyze(
        PSBT psbt,
        IReadOnlyDictionary<Script, string> ownedScripts,
        IReadOnlyDictionary<OutPoint, ChainInputFact> facts,
        ChainStatus chainStatus,
        bool canSign,
        Network network)
    {
        ArgumentNullException.ThrowIfNull(psbt);
        ArgumentNullException.ThrowIfNull(ownedScripts);
        ArgumentNullException.ThrowIfNull(facts);

        var tx = psbt.GetGlobalTransaction();
        var chainChecked = chainStatus == ChainStatus.Verified;

        // ─── Outputs (independent of every refusal, so the review can always show them) ───
        var outputs = new List<OutputAssessment>(tx.Outputs.Count);
        for (var i = 0; i < tx.Outputs.Count; i++)
            outputs.Add(AssessOutput(i, tx.Outputs[i], ownedScripts, network));

        // ─── Inputs ───
        var inputs = new List<InputAssessment>(psbt.Inputs.Count);
        var scripts = new Script?[psbt.Inputs.Count];
        for (var i = 0; i < psbt.Inputs.Count; i++)
        {
            var input = psbt.Inputs[i];
            facts.TryGetValue(input.PrevOut, out var fact);
            var psbtTxOut = PsbtTxOut(input);
            var script = psbtTxOut?.ScriptPubKey
                ?? (fact is { Lookup: ChainLookup.Found, Script: { } chainScript } ? chainScript : null);
            scripts[i] = script;

            var owner = script is null ? InputOwner.Unidentified
                : ownedScripts.ContainsKey(script) ? InputOwner.Wallet
                : InputOwner.Other;

            long? amount;
            AmountSource source;
            if (psbtTxOut is null)
            {
                amount = fact is { Lookup: ChainLookup.Found } ? fact.Amount : null;
                source = amount is null ? AmountSource.Unknown : AmountSource.Chain;
            }
            else if (owner == InputOwner.Wallet && chainChecked && fact is { Lookup: ChainLookup.Found, Amount: { } chainAmount })
            {
                amount = chainAmount;
                source = AmountSource.Chain;
            }
            else
            {
                amount = psbtTxOut.Value.Satoshi;
                source = AmountSource.PsbtOnly;
            }

            inputs.Add(new InputAssessment(
                i,
                owner,
                KindOf(script),
                amount,
                source,
                SignatureStateOf(input, owner),
                owner == InputOwner.Wallet && fact?.Confirmations == 0));
        }

        // ─── Totals ───
        var walletIn = inputs.Where(x => x.Owner == InputOwner.Wallet).Sum(x => x.Amount ?? 0);
        var walletOut = outputs.Where(o => o.IsWallet).Sum(o => o.Amount);
        var totalOut = outputs.Sum(o => o.Amount);
        long? totalIn = inputs.All(x => x.Amount is not null) ? inputs.Sum(x => x.Amount!.Value) : null;
        long? fee = totalIn - totalOut;

        var inputsToSign = inputs.Count(x => x.Owner == InputOwner.Wallet && x.SignatureState == SignatureState.Unsigned);
        var otherInputs = inputs.Count(x => x.Owner == InputOwner.Other);
        var hasWalletInput = inputs.Any(x => x.Owner == InputOwner.Wallet);

        // ─── Refusal ───
        var refusal = FindRefusal(psbt, tx, inputs, scripts, facts, chainChecked, fee);
        if (refusal is null && inputsToSign == 0)
            refusal = new PsbtRefusal(PsbtRefusalReason.NothingToSign, hasWalletInput
                ? "This wallet has already signed every input it can sign."
                : "None of this transaction's inputs belong to this wallet, so there is nothing for it to sign.");

        // ─── Fee status and warnings ───
        var feeStatus = chainChecked && otherInputs == 0 && fee is not null ? FeeStatus.Verified : FeeStatus.Unverified;
        var warnings = new List<PsbtWarning>();
        if (tx.LockTime.Value != 0) warnings.Add(PsbtWarning.LockTime);
        if (inputs.Any(x => x.IsUnconfirmed)) warnings.Add(PsbtWarning.UnconfirmedInput);
        if (outputs.Any(o => o.Label == OutputLabel.NonStandard)) warnings.Add(PsbtWarning.NonStandardOutput);
        if (fee is > 0 && IsHighFee(fee.Value, outputs, totalIn!.Value)) warnings.Add(PsbtWarning.HighFee);
        if (otherInputs > 0)
        {
            // Until shared transactions are checked (Story 4), other participants' amounts come from the PSBT alone.
            warnings.Add(PsbtWarning.UnverifiedFee);
            warnings.Add(PsbtWarning.SharedOutputsNotChecked);
            warnings.Add(PsbtWarning.OutOfRangeNote);
        }

        var isComplete = PsbtSigner.TryComplete(psbt).IsComplete;
        var blockingRefusal = refusal is not null && refusal.Reason != PsbtRefusalReason.NothingToSign;

        return new ReviewSummary
        {
            Inputs = inputs,
            Outputs = outputs,
            NetAmount = walletIn - walletOut,
            Fee = fee,
            FeeStatus = feeStatus,
            InputsToSign = inputsToSign,
            OtherInputs = otherInputs,
            Warnings = warnings,
            Refusal = refusal,
            ChainStatus = chainStatus,
            NeedsAcknowledgment = warnings.Contains(PsbtWarning.HighFee) || warnings.Contains(PsbtWarning.UnverifiedFee),
            CanSign = canSign && chainChecked && refusal is null && inputsToSign > 0,
            IsComplete = isComplete,
            CanBroadcastSigned = canSign && chainChecked && !blockingRefusal && isComplete && hasWalletInput,
        };
    }

    private static PsbtRefusal? FindRefusal(
        PSBT psbt,
        Transaction tx,
        IReadOnlyList<InputAssessment> inputs,
        IReadOnlyList<Script?> scripts,
        IReadOnlyDictionary<OutPoint, ChainInputFact> facts,
        bool chainChecked,
        long? fee)
    {
        if (DigiDollarScripts.IsDigiDollarVersion(tx.Version)
            || tx.Outputs.Any(o => DigiDollarScripts.TryParseOpReturn(o.ScriptPubKey, out _, out _)
                                   || DigiDollarScripts.TryParseMintOpReturn(o.ScriptPubKey, out _)))
            return new(PsbtRefusalReason.UnsupportedDigiDollar,
                "This is a DigiDollar transaction. Signing DigiDollar transactions here isn't supported.");

        for (var i = 0; i < psbt.Inputs.Count; i++)
            if (psbt.Inputs[i].SighashType is { } sighash && sighash != SigHash.All)
                return new(PsbtRefusalReason.UnsupportedSignatureType,
                    $"Input {i + 1} asks for a signature type other than the standard one. Only standard signatures are supported.", i);

        for (var i = 0; i < inputs.Count; i++)
            if (inputs[i].Owner == InputOwner.Unidentified)
                return new(PsbtRefusalReason.InputNotIdentified,
                    $"Input {i + 1} carries no details, and the network couldn't identify it.", i);

        for (var i = 0; i < scripts.Count; i++)
            if (scripts[i]!.IsScriptType(ScriptType.Taproot))
                return new(PsbtRefusalReason.UnsupportedTaproot,
                    $"Input {i + 1} is a Taproot input. Taproot inputs aren't supported.", i);

        for (var i = 0; i < inputs.Count; i++)
            if (inputs[i] is { Owner: InputOwner.Wallet, Kind: InputKind.Legacy } && psbt.Inputs[i].NonWitnessUtxo is null)
                return new(PsbtRefusalReason.MissingPreviousTransaction,
                    $"Input {i + 1} is a legacy input without its previous transaction, which is needed to sign it safely.", i);

        for (var i = 0; i < psbt.Inputs.Count; i++)
            if (!PreviousTransactionMatches(psbt.Inputs[i]))
                return new(PsbtRefusalReason.PreviousTransactionMismatch,
                    $"The previous transaction given for input {i + 1} doesn't match that input.", i);

        if (chainChecked)
        {
            for (var i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Owner != InputOwner.Wallet) continue;
                if (PsbtTxOut(psbt.Inputs[i]) is not { } claimed) continue;
                if (facts.TryGetValue(psbt.Inputs[i].PrevOut, out var fact)
                    && fact.Lookup == ChainLookup.Found
                    && ((fact.Amount is { } amount && amount != claimed.Value.Satoshi)
                        || (fact.Script is { } script && script != claimed.ScriptPubKey)))
                    return new(PsbtRefusalReason.AmountOrScriptMismatch,
                        $"The amount or address the PSBT gives for input {i + 1} doesn't match the network.", i);
            }

            for (var i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Owner != InputOwner.Wallet) continue;
                if (!facts.TryGetValue(psbt.Inputs[i].PrevOut, out var fact) || fact.IsUnspent != true)
                    return new(PsbtRefusalReason.InputSpentOrUnknown,
                        $"Input {i + 1} is already spent, or this wallet's network doesn't know it.", i);
            }
        }

        if (fee < 0)
            return new(PsbtRefusalReason.OutputsExceedInputs, "The outputs add up to more than the inputs.");

        return null;
    }

    /// <summary>The output an input spends, as the PSBT states it: the witness UTXO, else the previous transaction's output.</summary>
    private static TxOut? PsbtTxOut(PSBTInput input)
    {
        if (input.WitnessUtxo is { } witnessUtxo) return witnessUtxo;
        if (input.NonWitnessUtxo is { } previous && input.PrevOut.N < previous.Outputs.Count)
            return previous.Outputs[(int)input.PrevOut.N];
        return null;
    }

    private static bool PreviousTransactionMatches(PSBTInput input)
    {
        if (input.NonWitnessUtxo is not { } previous) return true;
        if (previous.GetHash() != input.PrevOut.Hash || input.PrevOut.N >= previous.Outputs.Count) return false;
        var spent = previous.Outputs[(int)input.PrevOut.N];
        return input.WitnessUtxo is not { } witnessUtxo
               || (witnessUtxo.Value == spent.Value && witnessUtxo.ScriptPubKey == spent.ScriptPubKey);
    }

    private static InputKind KindOf(Script? script) =>
        script is null ? InputKind.Other
        : PayToWitPubKeyHashTemplate.Instance.CheckScriptPubKey(script) ? InputKind.NativeSegWit
        : PayToPubkeyHashTemplate.Instance.CheckScriptPubKey(script) ? InputKind.Legacy
        : InputKind.Other;

    private static SignatureState SignatureStateOf(PSBTInput input, InputOwner owner) =>
        input.IsFinalized() ? SignatureState.Finalized
        : input.PartialSigs.Count == 0 ? SignatureState.Unsigned
        // The wallet's inputs are single-key scripts, so any signature on one is the wallet's.
        : owner == InputOwner.Wallet ? SignatureState.SignedByWallet
        : SignatureState.SignedByOthers;

    private static OutputAssessment AssessOutput(int index, TxOut output, IReadOnlyDictionary<Script, string> ownedScripts, Network network)
    {
        var script = output.ScriptPubKey;
        var amount = output.Value.Satoshi;

        if (script.Length > 0 && script.ToBytes()[0] == (byte)OpcodeType.OP_RETURN)
            return new OutputAssessment(index, amount, OutputLabel.Data, null, ReadableText(script), false);

        if (ownedScripts.TryGetValue(script, out var owned))
            return new OutputAssessment(index, amount, OutputLabel.Address, owned, null, true);

        var address = script.GetDestinationAddress(network);
        return address is null
            ? new OutputAssessment(index, amount, OutputLabel.NonStandard, null, null, false)
            : new OutputAssessment(index, amount, OutputLabel.Address, address.ToString(), null, false);
    }

    /// <summary>The data a data output carries, when it is printable UTF-8 text.</summary>
    private static string? ReadableText(Script script)
    {
        try
        {
            var data = script.ToOps().Skip(1).Where(op => op.PushData is { Length: > 0 }).SelectMany(op => op.PushData).ToArray();
            if (data.Length == 0) return null;
            var text = new UTF8Encoding(false, true).GetString(data);
            return text.All(c => !char.IsControl(c)) ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHighFee(long fee, IReadOnlyList<OutputAssessment> outputs, long totalIn)
    {
        var leaving = outputs.Where(o => !o.IsWallet).Sum(o => o.Amount);
        var basis = leaving > 0 ? leaving : totalIn;
        return fee * 100 > basis * HighFeePercent;
    }
}
