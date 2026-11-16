using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>The result of checking a payment destination.</summary>
public sealed record DestinationCheck(bool Ok, string? Error, bool IsOwnAddress);

/// <summary>What a scanned QR code or pasted text turned out to be.</summary>
public enum ScanKind
{
    Address,
    PaymentUri,
    Psbt,
    Unknown,
}

public sealed record ScanResult(ScanKind Kind, string? Address = null, decimal? AmountDgb = null, string? Label = null);

/// <summary>
/// The fee, change and limits for a proposed multisig payment. <see cref="Inputs"/> are the coins that would be spent.
/// </summary>
public sealed record SendEstimate(
    IReadOnlyList<UtxoInfo> Inputs,
    long AmountSatoshis,
    long FeeSatoshis,
    long ChangeSatoshis,
    long SpendableSatoshis,
    long MaxSendableSatoshis,
    string? Error)
{
    public bool Ok => Error is null;

    /// <summary>What stays in the multisig after this payment (change, or 0 when everything is sent).</summary>
    public long RemainingSatoshis => SpendableSatoshis - AmountSatoshis - FeeSatoshis;
}

/// <summary>
/// Pure helpers for the multisig Send page: destination checks, scan classification, and fee / Max / change maths
/// that match what the PSBT builder will actually produce.
/// </summary>
public static class MultisigSendHelper
{
    /// <summary>Outputs below this are refused (0.001 DGB, a conservative floor above the network's relay minimum).</summary>
    public const long DustThresholdSatoshis = 100_000;

    private const int OverheadBytes = 10;          // version, locktime, input and output counts
    private const int OutputBytes = 43;            // sized as a native SegWit script output, the largest usual kind
    private const int SignatureWitnessBytes = 73;  // push byte + DER signature + sighash flag

    // ─── Destination and scanning ───

    /// <summary>Checks that <paramref name="address"/> is a valid address for <paramref name="network"/>.</summary>
    public static DestinationCheck ValidateDestination(string? address, Network network, string? ownAddress = null)
    {
        var trimmed = address?.Trim() ?? "";
        if (trimmed.Length == 0)
            return new DestinationCheck(false, "Enter a destination address.", false);

        if (!AddressValidator.IsValid(trimmed, network))
        {
            var error = AddressValidator.IsValid(trimmed)
                ? "This address is for a different network."
                : "This is not a valid DigiByte address.";
            return new DestinationCheck(false, error, false);
        }

        var isOwn = !string.IsNullOrWhiteSpace(ownAddress)
                    && string.Equals(trimmed, ownAddress.Trim(), StringComparison.Ordinal);
        return new DestinationCheck(true, null, isOwn);
    }

    /// <summary>Works out whether scanned or pasted text is an address, a payment URI, a PSBT, or something else.</summary>
    public static ScanResult ClassifyScan(string? payload)
    {
        var text = payload?.Trim() ?? "";
        if (text.Length == 0) return new ScanResult(ScanKind.Unknown);

        if (text.StartsWith("digibyte:", StringComparison.OrdinalIgnoreCase))
        {
            var request = PaymentRequest.FromUri(text);
            return request is null || string.IsNullOrWhiteSpace(request.Address)
                ? new ScanResult(ScanKind.Unknown)
                : new ScanResult(ScanKind.PaymentUri, request.Address, request.AmountDgb, request.Label);
        }

        if (text.StartsWith("cHNidP", StringComparison.Ordinal))
            return new ScanResult(ScanKind.Psbt);

        if (AddressValidator.IsValid(text))
            return new ScanResult(ScanKind.Address, text);

        return new ScanResult(ScanKind.Unknown);
    }

    // ─── Size, fee, Max ───

    /// <summary>Virtual size of a spend with the given inputs, one payment output, an optional change output and an optional memo.</summary>
    public static int EstimateVBytes(int inputCount, int required, int total, string addressType, bool hasChange, int memoBytes = 0)
    {
        var witnessScriptBytes = 34 * total + 3;                                   // OP_M <33-byte keys> OP_N OP_CHECKMULTISIG
        var witnessPerInput = 2 + required * SignatureWitnessBytes + VarIntSize(witnessScriptBytes) + witnessScriptBytes;
        var nonWitnessPerInput = addressType == "p2wsh" ? 41 : 76;                 // wrapped SegWit adds a 35-byte scriptSig

        var outputs = 1 + (hasChange ? 1 : 0);
        var outputBytes = outputs * OutputBytes + (memoBytes > 0 ? 11 + Math.Min(memoBytes, 80) : 0);

        var weight = (OverheadBytes + inputCount * nonWitnessPerInput + outputBytes) * 4 + 2 + inputCount * witnessPerInput;
        return (weight + 3) / 4;
    }

    /// <summary>
    /// Picks coins (largest first) and works out the fee, change and what Max would be. Change below the dust
    /// threshold is not created; it is left in the fee. Pass <paramref name="max"/> to send everything.
    /// </summary>
    public static SendEstimate EstimateSend(
        IReadOnlyList<UtxoInfo> utxos,
        int feeRateSatPerVByte,
        long amountSatoshis,
        int required,
        int total,
        string addressType,
        int memoBytes = 0,
        bool max = false)
    {
        var rate = Math.Max(1, feeRateSatPerVByte);
        var coins = utxos.Where(u => u.AmountSatoshis > 0).OrderByDescending(u => u.AmountSatoshis).ToList();
        var spendable = coins.Sum(u => u.AmountSatoshis);

        long FeeFor(int inputs, bool change) => (long)EstimateVBytes(inputs, required, total, addressType, change, memoBytes) * rate;

        var maxFee = coins.Count == 0 ? 0 : FeeFor(coins.Count, false);
        var maxSendable = Math.Max(0, spendable - maxFee);

        SendEstimate Fail(string error, long amount = 0) =>
            new([], amount, 0, 0, spendable, maxSendable, error);

        if (spendable == 0)
            return Fail("There are no spendable funds in this multisig yet.");

        var amount = max ? maxSendable : amountSatoshis;
        if (amount <= 0)
            return Fail(max ? "The balance does not cover the network fee." : "Enter an amount.", amount);
        if (amount < DustThresholdSatoshis)
            return Fail($"The amount is below the minimum of {DustThresholdSatoshis / 100_000_000m:0.########} DGB.", amount);
        if (amount > maxSendable)
            return Fail($"The amount is more than the spendable balance of {spendable / 100_000_000m:N8} DGB (the network fee comes out of it).", amount);

        var selected = new List<UtxoInfo>();
        long sum = 0;
        foreach (var coin in coins)
        {
            selected.Add(coin);
            sum += coin.AmountSatoshis;
            if (sum >= amount + FeeFor(selected.Count, true)) break;
        }

        var feeWithChange = FeeFor(selected.Count, true);
        var change = sum - amount - feeWithChange;
        if (change >= DustThresholdSatoshis)
            return new SendEstimate(selected, amount, feeWithChange, change, spendable, maxSendable, null);

        // No usable change: whatever is left over goes to the fee, as long as it still covers the no-change fee.
        var feeNoChange = FeeFor(selected.Count, false);
        if (sum - amount < feeNoChange)
            return Fail($"The amount is more than the spendable balance of {spendable / 100_000_000m:N8} DGB (the network fee comes out of it).", amount);

        return new SendEstimate(selected, amount, sum - amount, 0, spendable, maxSendable, null);
    }

    private static int VarIntSize(int value) => value < 253 ? 1 : 3;
}
