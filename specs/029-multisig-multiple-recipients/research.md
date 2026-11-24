# Research: Multiple Recipients in a Multisig Payment

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-25

Every decision below was taken from reading the current code. No open questions remain.

## R1. Where "one recipient" is assumed today

**Decision**: Change these places, and nothing else:

| Place | What assumes one recipient |
|---|---|
| `DigiByte.Crypto/Multisig/MultisigService.CreateMultisigPSBTWithFee` | Takes one `destination` and one `amount`; adds one payment output |
| `DigiByte.Wallet/Services/MultisigSendHelper.EstimateVBytes` / `EstimateSend` | Sizes exactly one payment output; checks one amount against the minimum |
| `DigiByte.Wallet/Services/MultisigWalletService.CreateSpendingPSBTAsync` | One address + one amount in, one saved |
| `DigiByte.Wallet/Services/MultisigProposalReader.Read` | Adds the problem "pays more than one recipient, which online signing does not support"; `ProposalFacts` has one `DestinationAddress`/`AmountSat` |
| `MultisigWalletService.CheckProposalMatches` / `NewPendingFromFacts` | Compares and copies one address + amount |
| `DigiByte.Wallet/Models/MultisigModels.PendingMultisigTransaction` | Stores `DestinationAddress` + `AmountSatoshis` |
| `MultisigActivityBuilder.FromProposal` / `ActivityEntry` | Sets `Counterparty` only, `MoreCount` stays 0; no list of recipients for the detail sheet |
| `PaymentSummaryBuilder` / `PaymentSummaryModel` / `PaymentSummary.razor` | One recipient block |
| `MultisigSend.razor`, `PendingCard.razor`, `MultisigPending.razor`, `MultisigPendingDetail.razor`, `MultisigSigningRoom.razor` | Show `DestinationAddress` and the single amount |

**Rationale**: Chain-side activity (`MultisigActivityBuilder.FromChain`) already sums every external output and fills `MoreCount`, and `ActivityItem.razor` / `MultisigActivity.razor` already print "+N more". The signing-room relay (`DigiByte.Api`, `DigiByte.P2P.Shared`) never reads outputs: it carries the PSBT as an opaque string up to `SigningRoomRegistry.MaxPsbtBytes` (100 KB). A 20-recipient PSBT is a few kilobytes.

**Alternatives considered**: A separate "batch proposal" type next to the single one. Rejected: every list, card, room and import path would need two branches. A single-recipient proposal is simply a list of one.

## R2. Per-recipient minimum

**Decision**: Use the shared wallet's existing floor, `MultisigSendHelper.DustThresholdSatoshis` = 100,000 sat (0.001 DGB), for every recipient. The spec was corrected from 0.0001 DGB to match.

**Rationale**: Single-recipient multisig payments already refuse anything below 0.001 DGB (`SendProblem.BelowMinimum`). If multiple-recipients mode allowed 0.0001 DGB, the same page would have two minimums depending on a toggle. 0.001 DGB is also the threshold the builder uses for change, so the creating and reading rules stay the same.

**Alternatives considered**: 0.0001 DGB, as in the main wallet's `SendBatchAsync`. Rejected for the reason above. Lowering the multisig floor for everyone is a separate change.

## R3. Building the transaction

**Decision**: Add a `CreateMultisigPSBTWithFee` overload in `DigiByte.Crypto` that takes `IReadOnlyList<(BitcoinAddress Destination, Money Amount)>`. Outputs are written in the order the person entered them, then the memo (OP_RETURN), then change. The existing single-destination method calls the new one with a list of one, so its output is byte-for-byte the same.

**Rationale**: The builder already places coins, fee and change by hand (no `TransactionBuilder`), so adding N outputs is a loop. Keeping the order means the reader on a co-signer device lists recipients in the same order the proposer saw. Principle III requires `DigiByte.Crypto.Tests` coverage: N outputs, order kept, change under dust goes to the fee, refusal when the coins don't cover the total plus fee, and the single-recipient overload producing the same transaction as before.

**Alternatives considered**: Shuffling output order for privacy (BIP 69 ordering or random). Rejected for now: the single-recipient builder does not shuffle either, and a stable order makes review easier. It can be added later for both modes at once.

## R4. Fee and coin selection

**Decision**: Generalise `EstimateVBytes` with a `paymentOutputs` count (default 1), sized at the existing conservative 43 bytes each. Add an `EstimateSend` overload taking `IReadOnlyList<long> amountsSatoshis`. It checks each amount against the minimum, sums them, and then runs the same largest-first selection, dust-change and "leftover goes to fee" logic. `max` / quick shares stay single-recipient only. The old signature calls the new one with a list of one.

**Rationale**: The fee shown must be the fee built (FR-006). One selection routine for both modes means the numbers can't drift apart. On `OverSpendable` the estimate also carries `ShortfallSatoshis` (total + fee − spendable), so the page can say by how much the shared wallet is short (spec US1 scenario 7). Row errors on the page come only from `ValidateRecipients`. The estimate's `ProblemIndex` (set for `BelowMinimum` / `NoAmount`) is a backstop for callers that skip validation, such as `CreateSpendingPSBTAsync`.

**Alternatives considered**: Estimating with NBitcoin's `TransactionBuilder`. Rejected: the current code deliberately avoids it so that what is shown is what is built.

## R5. Reading a received proposal

**Decision**: `MultisigProposalReader.Read` returns `Recipients` (every external output in transaction order, as `ProposalRecipient(Address, AmountSatoshis)`), plus `Warnings` for the cases FR-013a lists:

- `RecipientBelowMinimum(index)` for an amount under 0.001 DGB
- `RecipientRepeated(index)` for the second and later lines paying the same address
- `TooManyRecipients` for more than 20 lines

Warnings never go into `Problems`, so they never block. The ">1 recipient" problem is removed. "No recipient" and "address can't be read on this network" stay as `Problems`. `DestinationAddress` / `AmountSat` stay on `ProposalFacts` as the first recipient and the total, so callers that only need a headline keep working.

**Rationale**: Everything shown and signed still comes from the transaction itself (FR-011). Outputs paying the multisig's own script stay as change, as today, so the shared wallet's own address can never show up as a "recipient".

**Alternatives considered**: Refusing rule-breaking proposals. The clarification chose to accept them with warnings.

## R6. Saving proposals and keeping old ones readable

**Decision**: `PendingMultisigTransaction` gains `List<ProposalRecipient> Recipients` (default empty). It is stored with the rest of the record as JSON in the existing secure-storage key. `DestinationAddress` becomes the first recipient and `AmountSatoshis` the total. A computed `AllRecipients` returns `Recipients`, or a one-item list built from `DestinationAddress` / `AmountSatoshis` when `Recipients` is empty (records saved before this feature). `RecipientCount` comes from `AllRecipients`.

**Rationale**: Old records deserialize unchanged (FR-018), and no migration pass is needed. Every screen that already reads `AmountSatoshis` keeps showing the right total. `CheckProposalMatches` compares `AllRecipients` with the reader's `Recipients` in order, plus the fee.

**Alternatives considered**: Rewriting stored records on load to fill `Recipients`. Rejected: it adds writes for no gain, since the computed fallback is enough.

## R7. Too large for one QR code

**What exists today**: `MultisigPendingDetail.razor` (Share section) already shows the QR code only when `tx.PsbtBase64.Length <= 2200`. Otherwise it shows `ms.pendingDetail.share.too_large` ("This proposal is too large for a QR code. Use Copy or Save as file."), and Copy and Save as file are always shown above it. The pending-detail screen is the only place a proposal's QR code appears.

**Decision**: Keep that behaviour and its 2,200-character limit; don't add a second one. Move the inline number into one named check, `QrCapacity.FitsProposal(string? psbtBase64)` in `DigiByte.Wallet` (limit `MaxProposalChars = 2200`, about 5% under the 2,331-byte maximum at error-correction level M, so a QR code is never drawn near its limit). Use it in the pending detail, and unit-test it. Reword the existing `ms.pendingDetail.share.too_large` text (English and Spanish) to also name the signing room, which the same screen already offers. No new key is added.

**Rationale**: FR-015 is mostly in place already; it only needs to name the signing room. A 2-of-3 proposal that spends two coins to 20 recipients is about 1,800 characters unsigned and goes over 2,200 once two signatures are added. One that spends one coin unsigned is about 1,500 characters and still fits, so checks must use a signed copy or several coins (quickstart §3.3).

**Alternatives considered**: A new 2,331-byte limit plus a new message key. Rejected: two limits and two messages for one rule. Making `QrImage` catch QRCoder's "data too long" error is left out too: the only proposal QR code is already behind the check, and the other QR codes (addresses, keys, invite links) are short. Animated multi-part QR codes are out of scope per the clarification.

## R8. Older co-signer wallets

**Decision**: On the Done step after creating a proposal with two or more recipients, show a one-line notice (FR-016) that co-signers need an up-to-date wallet to open it. No version handshake.

**Rationale**: An older wallet already refuses these proposals with a clear reason (the R1 problem text), so nothing unsafe can happen. The notice tells the proposer about that ahead of time.

## R9. The Send page

**Decision**: Keep `MultisigSend.razor` as the page, with a "Multiple recipients" toggle shaped like the main Send page's (`send.batch.*` text reused where the meaning is identical). Move the row list into a new `Components/Multisig/RecipientRows.razor`. Each row reuses the existing `RecipientInput` (type, paste, scan, contacts) plus an amount field. The page keeps one scanner overlay and remembers which row asked for it (the same pattern as `batchScannerIndex` on the main Send page). Rows stay in memory when the toggle is turned off (spec edge case). The Review step lists every row. The Done step shows the total, the recipient count and the full list.

**Rationale**: `RecipientInput` already does validation display, contacts and payment-link amounts, and the page file is already 636 lines. Pulling the rows into a component keeps it readable.

## R10. Pending lists, room and activity

**Decision**:

- `PendingCard` / `MultisigPending` show "to N recipients" when there are two or more.
- `PaymentSummaryModel` gains `IReadOnlyList<PaymentSummaryRecipient> Recipients` (address, grouped address, amount, optional local contact name, warnings). `PaymentSummary.razor` shows one block for one recipient (unchanged) or a numbered list for several. It is used by the pending detail and the signing room.
- The sign-confirm prompts (`ms.pendingDetail.sign.confirm.title`, `ms.signing.confirm_question`) get "to N recipients" variants.
- `MultisigActivityBuilder.FromProposal` sets `MoreCount = RecipientCount - 1`.
- `ActivityEntry` gains `Recipients` (address + amount for each external output). `FromProposal` fills it from `AllRecipients`. `FromChain` fills it, for sent entries, from the external outputs it already sums. The activity detail sheet (`MultisigActivity.razor`) lists every line when there are two or more, so FR-017's "detail screens list every recipient" covers activity as well. The list row keeps "first address +N more".
- The pending detail passes the change amount read from the payment (`MultisigProposalReader.Read(...).ChangeSat`) to the summary. Today it passes none, so co-signers see the change as the spec requires (US2 scenario 3).

Names come only from the device's own contacts, looked up by address (FR-011a).

## R11. Text and languages

**Decision**: Every new key goes into both `wwwroot/locales/en.json` and `es.json` (FR-019, spec 028), under `ms.send.batch.*`, `ms.ui.paymentSummary.*`, `ms.ui.pendingCard.*`, `ms.signing.*` and `ms.pendingDetail.*`. Plural forms follow the existing `.one` / `.other` key pattern.
