# Contract: Wallet and Crypto Services

The in-process interfaces this feature adds or changes. Every existing single-recipient signature stays and forwards to the list form, so current callers and tests keep compiling and behaving the same.

## DigiByte.Crypto — `MultisigService`

```csharp
// New
PSBT CreateMultisigPSBTWithFee(
    Script redeemScript,
    IReadOnlyList<Coin> coins,
    IReadOnlyList<(BitcoinAddress Destination, Money Amount)> payments,
    BitcoinAddress changeAddress,
    long feeSatoshis,
    long dustThresholdSatoshis,
    string? memo = null);

// Existing: now forwards with payments = [(destination, amount)]
PSBT CreateMultisigPSBTWithFee(Script, IReadOnlyList<Coin>, BitcoinAddress destination, Money amount, BitcoinAddress, long, long, string?);
```

- Throws `ArgumentException` when `payments` is empty or any amount is ≤ 0.
- Throws `InvalidOperationException` when the coins don't cover the sum of the payments plus the fee.
- Output order: each payment in the given order, then the memo (if any), then change (only when it's ≥ the dust threshold).
- Never signs.

## DigiByte.Wallet — `MultisigSendHelper`

```csharp
public const int MaxRecipients = 20;

// New: checks every row; result has one entry per row (null = fine)
public static IReadOnlyList<RecipientProblem?> ValidateRecipients(
    IReadOnlyList<(string Address, long AmountSatoshis)> rows, Network network, string? ownAddress);

public enum RecipientProblem { AddressMissing, WrongNetwork, InvalidAddress, Repeated, OwnAddress, NoAmount, BelowMinimum }

// Changed: paymentOutputs defaults to 1
public static int EstimateVBytes(int inputCount, int required, int total, string addressType, bool hasChange, int memoBytes = 0, int paymentOutputs = 1);

// New
public static SendEstimate EstimateSend(IReadOnlyList<UtxoInfo> utxos, int feeRateSatPerVByte,
    IReadOnlyList<long> amountsSatoshis, int required, int total, string addressType, int memoBytes = 0);
// Existing single-amount overload: forwards with [amountSatoshis], except `max: true`, which keeps its current path.
```

`SendEstimate` gains `RecipientCount`, `ProblemIndex` and `ShortfallSatoshis` (see [data-model.md](../data-model.md)).

## DigiByte.Wallet — `MultisigActivityBuilder`

`ActivityEntry.Recipients` is filled for proposal entries (`AllRecipients`) and for sent chain entries (external outputs). `FromProposal` sets `MoreCount = RecipientCount - 1`.

## DigiByte.Wallet — `MultisigWalletService`

```csharp
// New
Task<PendingMultisigTransaction> CreateSpendingPSBTAsync(
    MultisigWalletConfig config,
    IReadOnlyList<(string Address, decimal AmountDgb)> recipients,
    int feeRateSatPerVByte = DefaultFeeRateSatPerVByte,
    string? memo = null,
    string? submissionToken = null);
// Existing single-recipient overload forwards with [(destinationAddress, amountDgb)].
```

- Refuses (`ArgumentException`): no recipients, more than 20, any row that `ValidateRecipients` flags, any amount with more than 8 decimals.
- Refuses (`InvalidOperationException`): the estimate fails (not enough funds and so on).
- Saves one `PendingMultisigTransaction` with `Recipients` filled, `DestinationAddress = Recipients[0].Address` and `AmountSatoshis` = the total.
- Repeating a call with the same `submissionToken` returns the first proposal (unchanged behaviour).

`CheckProposalMatches(pending, config, psbt)` now compares `pending.AllRecipients` with `facts.Recipients` (same count, same address and amount at each position) and the fee. On a mismatch the reason reads "This device's saved recipients, amounts or fee do not match the payment itself."

`ReceiveProposalAsync`, `AdoptProposalAsync`, `ImportSignedPSBTAsync` and `MergeRoomSignaturesAsync` keep their signatures. A proposal with several recipients now gets through the reader, and `NewPendingFromFacts` copies `facts.Recipients`.

## DigiByte.Wallet — `MultisigProposalReader`

`Read(...)` returns `ProposalFacts` with `Recipients` and `Warnings` ([data-model.md](../data-model.md)). It no longer adds a problem for more than one recipient.

## DigiByte.Wallet — `QrCapacity` (new)

```csharp
public static class QrCapacity
{
    public const int MaxProposalChars = 2200;            // today's inline limit in MultisigPendingDetail.razor
    public static bool FitsProposal(string? psbtBase64); // not empty and Length ≤ MaxProposalChars
}
```

Replaces the inline `tx.PsbtBase64.Length <= 2200` check; the behaviour stays the same.

## DigiByte.Wallet — `PaymentSummaryBuilder`

```csharp
// New
public static PaymentSummaryModel Build(IReadOnlyList<ProposalRecipient> recipients, long feeSat, string? memo, long? changeSat,
    IReadOnlyList<ProposalWarning>? warnings = null, Func<string, string?>? contactNameFor = null);
// Existing single-recipient Build forwards with one recipient.
```

## Not changed

- Signing-room hub and wire models (`DigiByte.Api`, `DigiByte.P2P.Shared`): the PSBT is opaque and under the 100 KB limit.
- `SignPendingTransactionAsync`, `FinalizeAndBroadcastAsync`, the co-signer key checks and spent-coin detection.
