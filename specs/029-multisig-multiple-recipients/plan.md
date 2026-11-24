# Implementation Plan: Multiple Recipients in a Multisig Payment

**Branch**: `feature/multisig-multiple-recipients` (to be created) | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/029-multisig-multiple-recipients/spec.md`

## Summary

A shared-wallet (multisig) payment proposal can pay 2 to 20 recipients in one transaction, built the way the main wallet's batch send is. Every co-signer reviews and signs it through the existing routes: QR code, file, copy, and the live signing room.

The approach is to treat a proposal as a list of recipients everywhere, with a single recipient being a list of one:

- **Crypto**: the PSBT builder takes a list of payments.
- **Wallet**:
  - the fee estimate sizes N outputs
  - the reader returns every external output, with non-blocking warnings
  - `PendingMultisigTransaction` stores a `Recipients` list, and old records fall back to their single line
- **Web**: the Send page gains a Multiple recipients toggle and a `RecipientRows` component, and the summary, cards, room and activity show the list. The pending detail already hides QR codes above 2,200 characters. That limit moves into one named check, and its message now also names the signing room.

The signing-room relay is unchanged.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (`net10.0`)

**Primary Dependencies**: NBitcoin 9.0.5 (PSBT, scripts), Blazor WebAssembly (UI), QRCoder (QR codes), xUnit (tests). No new packages.

**Storage**: Browser secure storage (encrypted IndexedDB through `ISecureStorage`), with pending proposals stored as JSON per shared wallet. One field is added and no migration is needed ([data-model.md](data-model.md)).

**Testing**: `tests/DigiByte.Crypto.Tests` (builder), `tests/DigiByte.Wallet.Tests` (helper, reader, service, summary, activity, QR capacity). Manual checks follow [quickstart.md](quickstart.md).

**Target Platform**: The PWA in mobile and desktop browsers (WebAssembly).

**Project Type**: Web application (static Blazor WASM client plus independent API services). Only the client libraries and the UI change.

**Performance Goals**: Totals and fee update as the person types (well under 100 ms for 20 rows, since it's pure arithmetic). No extra network calls: one UTXO fetch per Send page, as today.

**Constraints**:

- Everything shown and signed is read from the PSBT (constitution II).
- A proposal's QR code is shown only up to the existing 2,200-character limit (a QR code holds at most 2,331 bytes at error-correction level M).
- Relay payload limit is 100 KB (not a concern).
- All new text is in English and Spanish.

**Scale/Scope**: Up to 20 recipients per created proposal; received ones may have more (with a warning). The work touches about 5 wallet/crypto classes and about 8 Razor files, and adds 1 component.

## Constitution Check

*GATE: passes before research; re-checked after design (still passes).*

| Principle | Status | Note |
|---|---|---|
| I. Self-custody | Pass | No key, seed or password handling changes. Recipient names never leave the device (FR-011a). |
| II. In-browser crypto / relay limits | Pass | Building and signing stay in the browser. The relay keeps carrying an opaque PSBT; no hub or wire-model change. Each co-signer reads recipients from the transaction itself. The offline share/import path stays, and it is also the fallback for proposals too large for a QR code. |
| III. Test-first for Crypto & Wallet | Pass, applies | New tests written to fail first: the builder in `DigiByte.Crypto.Tests`; `ValidateRecipients`, the multi-amount `EstimateSend` (including the shortfall), reader recipients/warnings, `CreateSpendingPSBTAsync` with N recipients, signing and finalizing an N-output payment, `CheckProposalMatches` on reorder/amount change, the legacy-record fallback, activity `MoreCount`/`Recipients`, `PaymentSummaryBuilder` and `QrCapacity` in `DigiByte.Wallet.Tests`. Negative paths: wrong network, repeated address, own address, below minimum, under-funded, mismatch. |
| IV. Boundary discipline | Pass | Transaction building in `DigiByte.Crypto`; rules, reading and storage in `DigiByte.Wallet`; screens in `DigiByte.Web`. No new reference direction. |
| V. Resilience | Pass | No new backend dependency. If the UTXO fetch fails, the Send page degrades as it does today, and pending proposals still render from saved data. |
| Security: address handling | Pass | Every row is checked client-side with `ValidateDestination` (network version byte + checksum) before it's used. |
| Workflow: docs | Applies | Update `docs/PROCESS_FLOWS.md` (multisig send) and the Roadmap/Help entries for multisig batch send in the same PR. |

No violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/029-multisig-multiple-recipients/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── wallet-services.md
│   └── ui-contracts.md
├── checklists/requirements.md
└── tasks.md              # next: /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Crypto/Multisig/
└── MultisigService.cs                 # CreateMultisigPSBTWithFee(payments list) + single overload forwards

src/DigiByte.Wallet/
├── Models/
│   ├── MultisigModels.cs              # ProposalRecipient; PendingMultisigTransaction.Recipients/AllRecipients/RecipientCount
│   ├── MultisigUxModels.cs            # PaymentSummaryModel.Recipients, PaymentSummaryRecipient
│   └── MultisigActivity.cs            # ActivityEntry.Recipients
└── Services/
    ├── MultisigSendHelper.cs          # MaxRecipients, ValidateRecipients, EstimateVBytes(paymentOutputs), EstimateSend(amounts)
    ├── MultisigProposalReader.cs      # Recipients, Warnings; drop the ">1 recipient" problem
    ├── MultisigWalletService.cs       # CreateSpendingPSBTAsync(recipients), NewPendingFromFacts, CheckProposalMatches
    ├── MultisigActivityBuilder.cs     # FromProposal MoreCount; Recipients from proposals and chain
    ├── PaymentSummaryBuilder.cs       # Build(recipients, …)
    └── QrCapacity.cs                  # new

src/DigiByte.Web/
├── Components/Multisig/
│   ├── RecipientRows.razor            # new: 2–20 rows, each RecipientInput + amount
│   ├── PaymentSummary.razor           # list of recipients, warnings
│   └── PendingCard.razor              # "to N recipients"
├── Pages/
│   ├── MultisigSend.razor             # toggle, rows, review, done notice
│   ├── MultisigPending.razor          # banner/prompt wording
│   ├── MultisigPendingDetail.razor    # summary list + change, sign prompt, QrCapacity in place of the inline 2200
│   ├── MultisigSigningRoom.razor      # summary list, sign prompt
│   └── MultisigActivity.razor         # detail sheet lists every recipient
└── wwwroot/locales/{en,es}.json       # new keys

tests/DigiByte.Crypto.Tests/MultisigServiceTests.cs
tests/DigiByte.Wallet.Tests/
├── MultisigSendValidationTests.cs     # rows + multi estimate
├── MultisigProposalReaderTests.cs     # recipients + warnings
├── MultisigWalletServiceTests.cs      # create N, matches, legacy fallback
├── MultisigActivityBuilderTests.cs    # MoreCount
├── MultisigUxPaymentSummaryTests.cs   # list summary
└── QrCapacityTests.cs                 # new

docs/PROCESS_FLOWS.md, src/DigiByte.Web/Pages/Roadmap.razor, src/DigiByte.Web/Pages/Help.razor
```

**Structure Decision**: The existing monorepo layout. Work runs bottom-up: Crypto, then Wallet, then Web. There are no server changes.

## Implementation Order (for /speckit-tasks)

1. **Foundation (blocks everything)**: the Crypto builder overload with tests; `ProposalRecipient` and the `PendingMultisigTransaction` fields with legacy-fallback tests; `MultisigSendHelper` rows and the multi estimate with tests.
2. **Story 1 (create)**: `CreateSpendingPSBTAsync(recipients)` with tests; `RecipientRows`; Send page toggle, review and done; locale keys.
3. **Story 2 (review/sign)**: reader recipients and warnings with tests; `NewPendingFromFacts` / `CheckProposalMatches` with tests; finalize test; `PaymentSummaryBuilder`/`PaymentSummary` lists with change; detail and room prompts; `QrCapacity` replacing the inline limit, and the reworded too-large text.
4. **Story 3 (lists/activity)**: `PendingCard` / `MultisigPending` wording; activity `MoreCount` and `Recipients` with tests; the activity detail sheet lists every recipient.
5. **Story 4 (fill rows quickly)**: per-row scan routing, contacts and payment-link amounts (mostly from `RecipientInput`).
6. **Polish**: Spanish check, docs, quickstart walkthrough.

## Risks

- **Silent single-recipient regression**: guarded by a test that the single builder overload produces the same transaction as before, and by running the existing multisig suites unchanged.
- **Oversize proposals**: a 2-of-3 proposal with 20 recipients passes the existing 2,200-character QR limit once signed. The pending detail's existing fallback (Copy, Save as file) already covers this. The work only names the limit (`QrCapacity`), tests it, and adds the signing room to the message.
- **Mixed app versions**: older co-signer wallets refuse multi-recipient proposals with an existing clear reason. The proposer is warned on the Done screen.

## Complexity Tracking

Not needed. No constitution violations.
