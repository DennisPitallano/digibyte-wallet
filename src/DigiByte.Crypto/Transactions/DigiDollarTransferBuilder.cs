using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Transactions;

public enum DdBuildError
{
    InvalidRecipient,
    BelowMinimum,
    AboveMaximum,
    InsufficientDd,
    ChangeBelowMinimum,
    ChangeAboveMaximum,
    ChangeKeyRequired,
    InvalidInput,
    InsufficientDgbFee,
}

/// <summary>A DigiDollar transfer cannot be built; <see cref="Error"/> says why so the UI can show the right message.</summary>
public sealed class DdBuildException : Exception
{
    public DdBuildException(DdBuildError error, string message) : base(message) => Error = error;

    public DdBuildError Error { get; }
}

/// <summary>A confirmed DigiDollar output to spend (value 0 in DGB, <paramref name="AmountCents"/> in DigiDollar).</summary>
public sealed record DdInput(string Txid, int Vout, long AmountCents, DigiDollarKey Key);

/// <summary>A P2WPKH DGB output that pays the fee; the script is derived from <paramref name="PrivateKey"/>.</summary>
public sealed record DgbFeeInput(string Txid, uint Vout, long ValueSats, Key PrivateKey);

public sealed record DdTransferRequest(
    /// <summary>"mainnet" or "testnet".</summary>
    string Network,
    IReadOnlyList<DdInput> DdInputs,
    IReadOnlyList<DgbFeeInput> FeeInputs,
    string RecipientAddress,
    long AmountCents,
    /// <summary>Receives DD change; required whenever the inputs exceed the amount.</summary>
    DigiDollarKey? ChangeKey,
    /// <summary>Where leftover DGB (above dust) goes.</summary>
    Script DgbChangeScript,
    /// <summary>Wallet fee estimate in sat/vB; the fee is never below <see cref="DigiDollarTransferBuilder.MinFeeSats"/>.</summary>
    long FeeRateSatPerVByte);

public sealed record DdSpentOutpoint(string Txid, int Vout);

public sealed record DdTransferResult(
    Transaction Transaction,
    string Txid,
    string Hex,
    long FeeSats,
    long DdChangeCents,
    long DgbChangeSats,
    IReadOnlyList<DdSpentOutpoint> SpentOutpoints);

/// <summary>
/// Builds and signs a DigiDollar TRANSFER exactly the way DigiByte Core's builder shapes it (research R1, R4, R5, R15):
/// <c>nVersion = 0x02000770</c>; inputs are DD UTXOs first, then DGB fee UTXOs; outputs are the recipient DD output, an
/// optional DD change output, an optional DGB change output, then the single DD <c>OP_RETURN</c> carrying the cents.
/// DD inputs are key-path Schnorr spends with the tweaked key; the fee input is P2WPKH. Built by hand, not through
/// <c>TransactionBuilder</c>, which knows nothing about DD conservation or zero-value P2TR outputs.
/// Nothing is persisted or sent; the caller broadcasts the returned hex.
/// </summary>
public static class DigiDollarTransferBuilder
{
    /// <summary>Per-output minimum: $1.00 (applies to the change output too).</summary>
    public const long MinAmountCents = 100;

    /// <summary>Per-output maximum: $100,000.00.</summary>
    public const long MaxAmountCents = 10_000_000;

    /// <summary>Core's <c>MIN_DD_TX_FEE</c>: 0.1 DGB.</summary>
    public const long MinFeeSats = 10_000_000;

    /// <summary>Core adds a DGB change output only when it is at least this many sats; smaller leftovers go to the fee.</summary>
    public const long MinDgbChangeSats = 1000;

    public static DdTransferResult Build(DdTransferRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);

        var network = r.Network switch
        {
            "mainnet" => DigiByteNetwork.Mainnet,
            "testnet" => DigiByteNetwork.Testnet,
            _ => throw new DdBuildException(DdBuildError.InvalidInput, "DigiDollar supports mainnet and testnet only."),
        };

        if (!DigiDollarAddressValidator.TryDecode(r.RecipientAddress, r.Network, out _, out var recipientKey))
            throw new DdBuildException(DdBuildError.InvalidRecipient, "The recipient is not a valid DigiDollar address for this network.");

        if (r.AmountCents < MinAmountCents)
            throw new DdBuildException(DdBuildError.BelowMinimum, "The amount is below the $1.00 minimum.");
        if (r.AmountCents > MaxAmountCents)
            throw new DdBuildException(DdBuildError.AboveMaximum, "The amount is above the $100,000.00 maximum per transaction.");

        if (r.DdInputs is not { Count: > 0 })
            throw new DdBuildException(DdBuildError.InvalidInput, "At least one DigiDollar input is required.");
        if (r.DdInputs.Any(i => i.AmountCents <= 0))
            throw new DdBuildException(DdBuildError.InvalidInput, "DigiDollar inputs must have a positive amount.");
        if (r.DdInputs.Select(i => (i.Txid, i.Vout)).Distinct().Count() != r.DdInputs.Count)
            throw new DdBuildException(DdBuildError.InvalidInput, "The same DigiDollar output cannot be spent twice.");

        var ddIn = r.DdInputs.Sum(i => i.AmountCents);
        if (ddIn < r.AmountCents)
            throw new DdBuildException(DdBuildError.InsufficientDd, "Not enough DigiDollar for this amount.");

        var change = ddIn - r.AmountCents;
        if (change > 0)
        {
            if (change < MinAmountCents)
                throw new DdBuildException(DdBuildError.ChangeBelowMinimum,
                    "The remainder would be under $1.00. Send the full balance or leave at least $1.00.");
            if (change > MaxAmountCents)
                throw new DdBuildException(DdBuildError.ChangeAboveMaximum,
                    "The remainder would exceed the $100,000.00 per-output maximum. Send a larger amount.");
            if (r.ChangeKey is null)
                throw new DdBuildException(DdBuildError.ChangeKeyRequired, "A change key is required for the remaining DigiDollar.");
        }

        var ddOutputs = change > 0 ? 2 : 1;
        var selected = SelectFeeInputs(r, ddOutputs, out var feeNeeded);

        var tx = network.CreateTransaction();
        tx.Version = DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer);

        foreach (var i in r.DdInputs)
            tx.Inputs.Add(new OutPoint(uint256.Parse(i.Txid), (uint)i.Vout));
        foreach (var f in selected)
            tx.Inputs.Add(new OutPoint(uint256.Parse(f.Txid), f.Vout));

        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(recipientKey)));
        var amounts = new List<long> { r.AmountCents };
        if (change > 0)
        {
            tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(r.ChangeKey!.OutputKey.ToBytes())));
            amounts.Add(change);
        }

        var feeIn = selected.Sum(f => f.ValueSats);
        var dgbChange = feeIn - feeNeeded;
        if (dgbChange < MinDgbChangeSats) dgbChange = 0;
        if (dgbChange > 0) tx.Outputs.Add(new TxOut(Money.Satoshis(dgbChange), r.DgbChangeScript));

        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, amounts)));

        Sign(tx, r.DdInputs, selected);

        return new DdTransferResult(
            tx, tx.GetHash().ToString(), tx.ToHex(), feeIn - dgbChange, change, dgbChange,
            r.DdInputs.Select(i => new DdSpentOutpoint(i.Txid, i.Vout))
                .Concat(selected.Select(f => new DdSpentOutpoint(f.Txid, (int)f.Vout))).ToList());
    }

    // ------------------------------------------------------------------ fee inputs

    /// <summary>Largest-first until the inputs cover the fee for the resulting size (never below <see cref="MinFeeSats"/>).</summary>
    private static List<DgbFeeInput> SelectFeeInputs(DdTransferRequest r, int ddOutputs, out long feeNeeded)
    {
        var selected = new List<DgbFeeInput>();
        long total = 0;
        feeNeeded = MinFeeSats;

        foreach (var f in r.FeeInputs.OrderByDescending(f => f.ValueSats))
        {
            if (f.ValueSats <= 0) continue;
            selected.Add(f);
            total += f.ValueSats;
            feeNeeded = FeeFor(r.DdInputs.Count, selected.Count, ddOutputs, r.FeeRateSatPerVByte);
            if (total >= feeNeeded) return selected;
        }

        throw new DdBuildException(DdBuildError.InsufficientDgbFee,
            "Not enough DGB to pay the network fee. Add DGB and try again.");
    }

    private static long FeeFor(int ddInputs, int feeInputs, int ddOutputs, long feeRateSatPerVByte)
    {
        // Rough size: header, 58 vB per P2TR key-path input, 68 vB per P2WPKH input, 43 vB per DD output,
        // a P2WPKH change output, and the OP_RETURN, with Core's 35% safety margin.
        var vsize = Math.Ceiling((11 + 58.0 * ddInputs + 68.0 * feeInputs + 43.0 * ddOutputs + 31 + 20) * 1.35);
        return Math.Max(MinFeeSats, (long)vsize * Math.Max(0, feeRateSatPerVByte));
    }

    // ------------------------------------------------------------------ signing

    private static void Sign(Transaction tx, IReadOnlyList<DdInput> ddInputs, IReadOnlyList<DgbFeeInput> feeInputs)
    {
        // Every input's prevout (amount and script) feeds the BIP341 / BIP143 sighash.
        var spent = ddInputs
            .Select(i => new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(i.Key.OutputKey.ToBytes())))
            .Concat(feeInputs.Select(f => new TxOut(Money.Satoshis(f.ValueSats), f.PrivateKey.PubKey.WitHash.ScriptPubKey)))
            .ToArray();
        var pre = tx.PrecomputeTransactionData(spent);

        for (var i = 0; i < ddInputs.Count; i++)
        {
            var hash = tx.GetSignatureHashTaproot(pre, new TaprootExecutionData(i));
            var sig = ddInputs[i].Key.SignKeySpend(hash);
            tx.Inputs[i].WitScript = new WitScript(Op.GetPushOp(sig.ToBytes()));
        }

        for (var k = 0; k < feeInputs.Count; k++)
        {
            var index = ddInputs.Count + k;
            var key = feeInputs[k].PrivateKey;
            var hash = tx.GetSignatureHash(key.PubKey.Hash.ScriptPubKey, index, SigHash.All, spent[index], HashVersion.WitnessV0, pre);
            var sig = key.Sign(hash, new SigningOptions(SigHash.All, useLowR: true));
            tx.Inputs[index].WitScript = new WitScript(Op.GetPushOp(sig.ToBytes()), Op.GetPushOp(key.PubKey.ToBytes()));
        }
    }
}
