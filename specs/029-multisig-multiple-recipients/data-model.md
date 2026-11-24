# Data Model: Multiple Recipients in a Multisig Payment

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

Only one stored shape changes (`PendingMultisigTransaction`). Everything else is computed when it's needed and never stored or sent.

## ProposalRecipient (new, `DigiByte.Wallet/Models/MultisigModels.cs`)

One payment line of a proposal.

| Field | Type | Rules |
|---|---|---|
| `Address` | string | Valid for the active network; trimmed |
| `AmountSatoshis` | long | > 0. Created here: ≥ `MultisigSendHelper.DustThresholdSatoshis` (100,000) |

A `sealed record`. It has no name field: names never travel with a proposal (FR-011a).

## PendingMultisigTransaction (changed, stored)

Stored as JSON in secure storage under `MultisigPendingPrefix + walletId`, as today.

| Field | Change | Meaning |
|---|---|---|
| `Recipients` | **new** `List<ProposalRecipient>`, default `[]` | Every payment line in transaction order. Empty on records saved before this feature |
| `DestinationAddress` | meaning narrowed | First recipient's address (unchanged for one-recipient proposals) |
| `AmountSatoshis` | meaning widened | Total of all recipients (unchanged for one-recipient proposals) |
| `AllRecipients` | **new**, computed | `Recipients` when not empty, otherwise `[new(DestinationAddress, AmountSatoshis)]` |
| `RecipientCount` | **new**, computed | `AllRecipients.Count` |
| `Description` | text only | "Send {total} DGB to {short address}" for one recipient, "Send {total} DGB to {N} recipients" for several |

No migration is needed: an old record reads with `Recipients = []`, and `AllRecipients` falls back to the single line.

**Rule**: when `Recipients` is not empty, `DestinationAddress == Recipients[0].Address` and `AmountSatoshis == Recipients.Sum(AmountSatoshis)`. This is set at creation and on import (`NewPendingFromFacts`) and checked by tests.

Status changes (`AwaitingSignatures → ReadyToFinalize → Broadcast / Failed / Cancelled`) are unchanged.

## ProposalFacts (changed, computed by `MultisigProposalReader`)

| Field | Change |
|---|---|
| `Recipients` | **new** `IReadOnlyList<ProposalRecipient>`: external outputs in transaction order (outputs to the multisig's own script are change; OP_RETURN is the memo) |
| `Warnings` | **new** `IReadOnlyList<ProposalWarning>`: never blocks |
| `DestinationAddress` / `AmountSat` | first recipient / total |
| `Problems` | the "more than one recipient" problem is removed; the others stay |

## ProposalWarning (new, computed)

`sealed record ProposalWarning(ProposalWarningKind Kind, int? RecipientIndex)`

| Kind | When | `RecipientIndex` |
|---|---|---|
| `RecipientBelowMinimum` | amount < 100,000 sat | that line |
| `RecipientRepeated` | address already paid by an earlier line (exact string match after reading from the script) | the later line |
| `TooManyRecipients` | more than 20 lines | null |

Screens put each warning into words in the person's language; the record carries no English text.

## SendEstimate (changed, computed)

| Field | Change |
|---|---|
| `AmountSatoshis` | total of all recipients |
| `RecipientCount` | **new**, default 1 |
| `ProblemIndex` | **new** `int?`: the row a per-row problem belongs to (`BelowMinimum`, `NoAmount`). A backstop only: the Send page shows row errors from `ValidateRecipients` |
| `ShortfallSatoshis` | **new** `long`: on `OverSpendable`, total + fee − spendable (0 otherwise) |

## Send form rows (UI state only, `MultisigSend.razor` / `RecipientRows.razor`)

`RecipientRow { Address, AmountText, Error, DestinationCheck? }`. There are 2 to 20 rows, kept in memory while the page is open. They are checked by `MultisigSendHelper.ValidateRecipients` (see [contracts/wallet-services.md](contracts/wallet-services.md)): missing or invalid address, wrong network, repeated (case-insensitive), the shared wallet's own address, missing amount, more than 8 decimals, below 0.001 DGB.

## PaymentSummaryModel (changed, computed)

| Field | Change |
|---|---|
| `Recipients` | **new** `IReadOnlyList<PaymentSummaryRecipient>` |
| `AmountSat` | total |
| `RecipientAddress` / `RecipientGrouped` | first recipient (kept so the one-recipient layout doesn't change) |

`PaymentSummaryRecipient(string Address, string Grouped, long AmountSat, string? ContactName, IReadOnlyList<ProposalWarningKind> Warnings)`. `ContactName` comes only from this device's contacts.

## ActivityEntry (changed, computed)

| Field | Change |
|---|---|
| `Recipients` | **new** `IReadOnlyList<ProposalRecipient>`, default `[]`. Proposal entries: `AllRecipients`. Sent chain entries: each external output (address + amount) in transaction order. Received entries: empty |
| `MoreCount` | `FromProposal` now sets `RecipientCount - 1`. Chain entries already set it |

## QrCapacity (new, `DigiByte.Wallet/Services/QrCapacity.cs`)

`MaxProposalChars = 2200` (the limit `MultisigPendingDetail.razor` already uses inline). `FitsProposal(string? psbtBase64)` is true when the text is not empty and has at most that many characters.
