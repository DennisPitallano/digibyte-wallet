---
description: "Task list for Multiple Recipients in a Multisig Payment"
---

# Tasks: Multiple Recipients in a Multisig Payment

**Input**: Design documents from `specs/029-multisig-multiple-recipients/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. Constitution Principle III requires tests for every change to `DigiByte.Crypto` and `DigiByte.Wallet`, written in the same PR and failing before the code change. Each test task comes before the implementation task it covers.

**Organization**: Tasks are grouped by user story so each can be built and checked on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1–US4)

---

## Phase 1: Setup

**Purpose**: Branch and baseline.

- [X] T001 Create branch `feature/multisig-multiple-recipients` from `main`, then run `dotnet build DigiByte.slnx`, `dotnet test tests/DigiByte.Crypto.Tests` and `dotnet test tests/DigiByte.Wallet.Tests` and note the passing baseline (no code change)

---

## Phase 2: Foundational (blocks every story)

**Purpose**: The list-of-recipients shape in the builder, the stored proposal and the send helper. Every story uses these.

- [X] T002 [P] Write failing tests in `tests/DigiByte.Crypto.Tests/MultisigServiceTests.cs` for the new `CreateMultisigPSBTWithFee(redeemScript, coins, IReadOnlyList<(BitcoinAddress Destination, Money Amount)> payments, changeAddress, feeSatoshis, dustThresholdSatoshis, memo)`:
  - three payments produce three outputs in the given order, then the OP_RETURN memo, then change
  - change below `dustThresholdSatoshis` is not added (it stays in the fee)
  - coins that don't cover the sum plus the fee throw `InvalidOperationException`
  - empty `payments` or any amount ≤ 0 throws `ArgumentException`
  - the existing single-destination overload produces the same unsigned transaction hash as before the change (capture the hash from the current code first)
- [X] T003 Implement the payments-list overload in `src/DigiByte.Crypto/Multisig/MultisigService.cs` and make the single-destination overload forward to it with `[(destination, amount)]`. Never sign (per [contracts/wallet-services.md](contracts/wallet-services.md))
- [X] T004 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigModelsTests.cs`:
  - a `PendingMultisigTransaction` JSON saved before this feature (no `Recipients`) deserializes with `Recipients = []`, `AllRecipients` = one line `(DestinationAddress, AmountSatoshis)`, and `RecipientCount = 1`
  - a record with three `Recipients` round-trips through `System.Text.Json` unchanged
- [X] T005 Add `public sealed record ProposalRecipient(string Address, long AmountSatoshis)` and, on `PendingMultisigTransaction`:
  - `List<ProposalRecipient> Recipients { get; init; } = []`
  - computed `AllRecipients` ("`Recipients` when not empty, otherwise `[new(DestinationAddress, AmountSatoshis)]`")
  - computed `RecipientCount`
  - updated doc comments: `DestinationAddress` = first recipient, `AmountSatoshis` = total of all recipients

  All in `src/DigiByte.Wallet/Models/MultisigModels.cs`.
- [X] T006 [P] Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigSendValidationTests.cs`:
  - `ValidateRecipients` returns, per row, `AddressMissing`, `WrongNetwork`, `InvalidAddress`, `Repeated` (case-insensitive, on the second and later rows), `OwnAddress`, `NoAmount`, `BelowMinimum` (< 100,000 sat), or null
  - `EstimateVBytes(..., paymentOutputs: 3)` is 2 × 43 bytes heavier than `paymentOutputs: 1`
  - `EstimateSend(utxos, rate, [a, b, c], ...)` has `AmountSatoshis = a+b+c` and `RecipientCount = 3`, and its fee matches a 3-output transaction
  - a row below the minimum gives `Problem = BelowMinimum` with `ProblemIndex` set to that row, and a zero-amount row gives `Problem = NoAmount` with `ProblemIndex` set to that row
  - a total over the spendable balance gives `OverSpendable` with `ShortfallSatoshis` = total + fee − spendable (and `ShortfallSatoshis = 0` on success)
  - the existing single-amount `EstimateSend` (including `max: true`) returns exactly what it did before
- [X] T007 Implement in `src/DigiByte.Wallet/Services/MultisigSendHelper.cs`:
  - `public const int MaxRecipients = 20`
  - `enum RecipientProblem { AddressMissing, WrongNetwork, InvalidAddress, Repeated, OwnAddress, NoAmount, BelowMinimum }`
  - `ValidateRecipients(IReadOnlyList<(string Address, long AmountSatoshis)> rows, Network network, string? ownAddress)`, reusing `ValidateDestination`
  - the `paymentOutputs = 1` parameter on `EstimateVBytes`
  - the `EstimateSend(IReadOnlyList<UtxoInfo>, int, IReadOnlyList<long> amountsSatoshis, int required, int total, string addressType, int memoBytes = 0)` overload with the same largest-first selection, dust-change and leftover-to-fee rules
  - `SendEstimate.RecipientCount` (default 1), `ProblemIndex` (`int?`) and `ShortfallSatoshis` (`long`, set on `OverSpendable` to total + fee − spendable)
  - the single-amount overload forwards with `[amountSatoshis]`, except the `max: true` path

**Checkpoint**: The builder, the stored model and the helper understand several recipients, and all existing tests still pass.

---

## Phase 3: User Story 1 — Propose one payment to several recipients (Priority: P1) 🎯 MVP

**Goal**: From a shared wallet's Send page, turn on Multiple recipients, fill 2–20 rows, review, and create one proposal that pays them all.

**Independent Test**: In a testnet 2-of-3 wallet, create a 3-recipient proposal. One pending proposal exists whose PSBT pays each address its amount, with change back to the shared wallet (quickstart §1).

- [X] T008 [P] [US1] Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigWalletServiceTests.cs` for `CreateSpendingPSBTAsync(config, IReadOnlyList<(string Address, decimal AmountDgb)> recipients, feeRate, memo, submissionToken)`:
  - three recipients produce a saved proposal whose PSBT has the three outputs in order, with `Recipients` filled, `DestinationAddress == Recipients[0].Address`, `AmountSatoshis` = the sum, and `Description` "Send {total} DGB to 3 recipients"
  - refused with `ArgumentException`: empty list; 21 recipients; a repeated address; the shared wallet's own address; an amount below 0.001 DGB; an amount with more than 8 decimals
  - under-funded is refused with `InvalidOperationException`
  - the same `submissionToken` returns the first proposal
  - the single-recipient overload still saves the same proposal it did before
- [X] T009 [US1] Implement the recipients-list `CreateSpendingPSBTAsync` in `src/DigiByte.Wallet/Services/MultisigWalletService.cs`. It validates with `MultisigSendHelper.ValidateRecipients`, estimates with the multi-amount `EstimateSend`, builds with the T003 overload, and saves with the fields above. The single-recipient overload forwards with a list of one
- [X] T010 [P] [US1] Add the Send-page keys to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`:
  - toggle, explainer, "N / 20 recipients", "Recipient N", add, remove
  - review heading, total, total with fee, remaining
  - "to N recipients" (`.one`/`.other`)
  - the notice "Co-signers need an up-to-date wallet to open a payment to several recipients"
  - one message per `RecipientProblem`
  - "Not enough in this shared wallet: {0} DGB short"

  Prefix them `ms.send.batch.*` and reuse the existing `send.batch.*` keys only where the wording is identical.
- [X] T011 [US1] Create `src/DigiByte.Web/Components/Multisig/RecipientRows.razor`:
  - a numbered list of 2–20 rows, each an existing `RecipientInput` (address with paste, contacts and scan request) plus an amount field
  - a counter "N / 20"
  - "+ Add recipient", hidden at `MultisigSendHelper.MaxRecipients`
  - remove only when there are more than 2 rows
  - each row's error shown under it
  - raises `RowsChanged` and `OnScanRequested(int rowIndex)`
  - styled per `docs/UI_STYLE.md`
- [X] T012 [US1] Update `src/DigiByte.Web/Pages/MultisigSend.razor`:
  - add the Multiple recipients toggle and "?" explainer (as on `Pages/Send.razor`) and render `RecipientRows` when it's on
  - hide Max and the quick shares in that mode; keep the fee rate and memo
  - keep the rows in memory when the toggle is turned off
  - recalculate on every change with `ValidateRecipients` plus the multi-amount `EstimateSend`. Row errors come only from `ValidateRecipients` (not the estimate's `ProblemIndex`) and appear once a row has content. On `OverSpendable`, show the shortfall message with `ShortfallSatoshis`
  - show total, fee, total with fee and remaining
  - disable Continue when any row has an error, there are fewer than 2 rows, or the estimate fails
- [X] T013 [US1] In the Review step of `src/DigiByte.Web/Pages/MultisigSend.razor`, list every recipient as "Recipient N", with a name from this device's contacts when known, the grouped address and the amount. Then show the total, network fee, total with fee and remaining
- [X] T014 [US1] In `Confirm()` and the Done step of `src/DigiByte.Web/Pages/MultisigSend.razor`, call the recipients-list `CreateSpendingPSBTAsync` in multi mode. Show the total, "to N recipients", the list, and the up-to-date-wallet notice (only when N ≥ 2). Keep sign now, share and room actions as they are

**Checkpoint**: A multi-recipient proposal can be created and is listed (with today's single-recipient wording, until US3).

---

## Phase 4: User Story 2 — Co-signers review and sign a multi-recipient proposal (Priority: P1)

**Goal**: A multi-recipient proposal opens on every co-signer's device by QR code, file, paste or signing room, and shows every recipient read from the PSBT. Rule-breaking lines get warnings, mismatches are refused, and it signs and broadcasts.

**Independent Test**: Import a 3-recipient proposal on a second co-signer by each route. Every recipient shows, and it signs and broadcasts. A signed proposal over 2,200 characters shows no QR code, only Copy and Save as file, with the message naming the signing room (quickstart §3–§5).

- [X] T015 [P] [US2] Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigProposalReaderTests.cs`:
  - a 3-recipient PSBT gives `Recipients` in transaction order, with change (outputs to the multisig script) and the OP_RETURN memo left out
  - `DestinationAddress` = first and `AmountSat` = total
  - no problem for more than one recipient
  - warnings: `RecipientBelowMinimum` (index) for < 100,000 sat, `RecipientRepeated` (index of the later line), `TooManyRecipients` (null index) for 21 lines
  - warnings never appear in `Problems`
  - "no recipient" and "address can't be read on this network" are still problems
- [X] T016 [US2] Implement in `src/DigiByte.Wallet/Services/MultisigProposalReader.cs`:
  - `ProposalFacts.Recipients` and `ProposalFacts.Warnings`
  - `sealed record ProposalWarning(ProposalWarningKind Kind, int? RecipientIndex)` with `ProposalWarningKind { RecipientBelowMinimum, RecipientRepeated, TooManyRecipients }`
  - remove the "more than one recipient" problem
  - update the class summary
- [X] T017 [P] [US2] Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigProposalReceiveTests.cs` and `MultisigPendingImportTests.cs`:
  - receiving a 3-recipient PSBT saves a pending proposal with `Recipients` copied, `DestinationAddress` = first and `AmountSatoshis` = total
  - `CheckProposalMatches` refuses when the saved recipients are reordered, an amount differs, or the fee differs, with the message "This device's saved recipients, amounts or fee do not match the payment itself."
  - a legacy single-recipient record still matches its PSBT
  - signatures merge into a 3-recipient proposal as they do for one recipient
- [X] T018 [US2] Update `NewPendingFromFacts` and `CheckProposalMatches` in `src/DigiByte.Wallet/Services/MultisigWalletService.cs`:
  - copy `facts.Recipients`
  - set `Description` to "Send {total} DGB to {N} recipients" when there are two or more (the same wording T009 uses; whichever of T009/T018 lands first adds a shared private helper for it in this file)
  - compare `pending.AllRecipients` with `facts.Recipients` position by position, plus the fee
- [X] T019 [P] [US2] Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigUxPaymentSummaryTests.cs` for the new `PaymentSummaryBuilder.Build(recipients, feeSat, memo, changeSat, warnings, contactNameFor)`:
  - one entry per recipient, with grouped address, amount, the name from `contactNameFor` (null when unknown) and the warnings for its index
  - `AmountSat` = total and `TotalOutSat` = total + fee
  - the existing single-recipient `Build` returns the same model as before (plus a one-item `Recipients`)
- [X] T020 [US2] Add `PaymentSummaryRecipient(string Address, string Grouped, long AmountSat, string? ContactName, IReadOnlyList<ProposalWarningKind> Warnings)` and `PaymentSummaryModel.Recipients` in `src/DigiByte.Wallet/Models/MultisigUxModels.cs`, and implement the list `Build` in `src/DigiByte.Wallet/Services/PaymentSummaryBuilder.cs`. The single `Build` forwards
- [X] T021 [P] [US2] Write failing tests in a new `tests/DigiByte.Wallet.Tests/QrCapacityTests.cs`: `FitsProposal` is true at exactly 2,200 characters, false at 2,201, and false for null or empty
- [X] T022 [US2] After T021, create `src/DigiByte.Wallet/Services/QrCapacity.cs` with `public const int MaxProposalChars = 2200` (the limit `MultisigPendingDetail.razor` uses inline today) and `public static bool FitsProposal(string? psbtBase64)`
- [X] T023 [P] [US2] Add the review keys to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`:
  - recipient list heading
  - warnings "Below the 0.001 DGB minimum", "This address is paid more than once", "This payment has more than 20 recipients"
  - sign prompts "Sign the payment of {0} DGB to {1} recipients?" (pending detail and room)
  - reword the existing `ms.pendingDetail.share.too_large` (do not add a new key) to "This proposal is too large for a QR code. Use Copy, Save as file, or sign together in a signing room." and its Spanish equivalent
- [X] T024 [US2] Update `src/DigiByte.Web/Components/Multisig/PaymentSummary.razor` to keep the current single-recipient layout when `Recipients.Count == 1`. For several, show a numbered list (name when present, grouped address with its own copy button, amount, amber warning lines) and put the `TooManyRecipients` warning above the list
- [X] T025 [US2] Update `src/DigiByte.Web/Pages/MultisigPendingDetail.razor`:
  - build the summary with the list `Build` from `pending.AllRecipients`, the reader's warnings and `ChangeSat` for that PSBT (today the change argument is `null`), and a lookup in this device's contacts
  - use the N-recipient sign prompt when `RecipientCount ≥ 2`
  - replace the inline `tx.PsbtBase64.Length <= 2200` in the Share section with `QrCapacity.FitsProposal(tx.PsbtBase64)`. Keep the existing Copy / Save as file buttons and the `too_large` line exactly where they are
- [X] T026 [US2] Write a test in `tests/DigiByte.Wallet.Tests/MultisigWalletServiceTests.cs` (after T018; run it after T008 when US1 is worked at the same time, since both edit this file) that builds a 3-recipient PSBT with the T003 builder, receives it into a 2-of-3 wallet with `ReceiveProposalAsync` (so it doesn't need US1's T009), signs it with two co-signer keys (`SignPendingTransactionAsync`), and calls `FinalizeAndBroadcastAsync` against `MultisigFakeChain` in `tests/DigiByte.Wallet.Tests/MultisigTestDoubles.cs` (add a `LastBroadcastHex` property there that records what `BroadcastTransactionAsync` received; it records nothing today). The broadcast transaction must have the three payment outputs with their exact amounts plus change, and the proposal must be marked sent (FR-014)
- [X] T027 [US2] Update `src/DigiByte.Web/Pages/MultisigSigningRoom.razor` to build the summary from `facts.Recipients` and `facts.Warnings` (names only from this device's contacts) and to use the N-recipient confirm prompt

**Checkpoint**: Stories 1 and 2 together give the full create → share → sign → broadcast flow.

---

## Phase 5: User Story 3 — Multi-recipient payments in lists and activity (Priority: P2)

**Goal**: Pending cards, banners and activity read as one payment to N recipients.

**Independent Test**: One pending and one sent 3-recipient payment show the total and "to 3 recipients" / "+2 more", and their activity detail lists all three recipients; single-recipient ones look unchanged (quickstart §2).

- [X] T028 [P] [US3] Write failing tests in `tests/DigiByte.Wallet.Tests/MultisigActivityBuilderTests.cs`:
  - a proposal with 3 `Recipients` gives an entry with `Counterparty` = the first address, `MoreCount = 2`, `AmountSatoshis` = the total and `Recipients` = the three lines in order
  - a legacy single-recipient proposal gives `MoreCount = 0` and one `Recipients` line
  - a sent chain transaction with three external outputs plus change gives three `Recipients` (change left out); a received one gives none
- [X] T029 [US3] Add `IReadOnlyList<ProposalRecipient> Recipients { get; init; } = []` to `ActivityEntry` in `src/DigiByte.Wallet/Models/MultisigActivity.cs`. In `src/DigiByte.Wallet/Services/MultisigActivityBuilder.cs`, make `FromProposal` set `MoreCount = Math.Max(0, p.RecipientCount - 1)` and `Recipients = p.AllRecipients`, and make `FromChain` set `Recipients` for sent entries from the external outputs it already sums
- [X] T030 [P] [US3] Add the keys "to {0} recipients" (`.one`/`.other`) for the card, banner and restore prompt to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`
- [X] T031 [US3] Update `src/DigiByte.Web/Components/Multisig/PendingCard.razor` to show "to N recipients" instead of the short address when `RecipientCount ≥ 2`, in both the visible text and the screen-reader label
- [X] T032 [US3] Update the received-proposal banner and the restore-cancelled prompt in `src/DigiByte.Web/Pages/MultisigPending.razor` to use the same N-recipient wording when `RecipientCount ≥ 2`
- [X] T033 [US3] In the detail sheet of `src/DigiByte.Web/Pages/MultisigActivity.razor`, when `e.Recipients.Count ≥ 2`, list every address (grouped, with copy) and amount under "To", then the total and the fee. Entries with fewer recipients keep today's "To … +N more" line

**Checkpoint**: Every place a proposal appears reads correctly for one or many recipients.

---

## Phase 6: User Story 4 — Fill recipient rows quickly (Priority: P3)

**Goal**: Each row can be filled by scan, payment link (with amount), paste or contact, touching only that row.

**Independent Test**: Fill row 1 from a contact, row 2 by scanning a `digibyte:` link with an amount, and row 3 by pasting. Each lands in its own row only (quickstart §1.2).

- [X] T034 [US4] In `src/DigiByte.Web/Pages/MultisigSend.razor`, keep the one scanner overlay and remember the row that asked (`scannerRowIndex`, as `batchScannerIndex` does on `Pages/Send.razor`). Route the scan result to that row's `RecipientInput.ApplyScannedAsync`. A payment link's amount fills only that row's amount. The `?address=&amount=` query prefill keeps filling the single-recipient form
- [X] T035 [US4] In `src/DigiByte.Web/Components/Multisig/RecipientRows.razor`, handle each row's `OnPaymentLink` (a pasted `digibyte:` link with an amount sets that row's amount) and contact pick (sets that row's address and clears its error). Other rows stay unchanged

**Checkpoint**: All four stories work.

---

## Phase 7: Polish & Cross-Cutting

- [X] T036 [P] Describe multi-recipient proposals (create, review with warnings, the QR fallback, the up-to-date-wallet notice) in the multisig send flow in `docs/PROCESS_FLOWS.md`
- [X] T037 [P] Add a multisig batch-send entry to `src/DigiByte.Web/Pages/Roadmap.razor` and a short answer to `src/DigiByte.Web/Pages/Help.razor`, handling text the way each file already does
- [X] T038 Check that `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json` have the same new keys, and that no touched Razor file (`MultisigSend`, `RecipientRows`, `PaymentSummary`, `PendingCard`, `MultisigPending`, `MultisigPendingDetail`, `MultisigSigningRoom`, `MultisigActivity`) has hard-coded English
- [X] T039 Run `dotnet build DigiByte.slnx`, `dotnet test tests/DigiByte.Crypto.Tests` and `dotnet test tests/DigiByte.Wallet.Tests`. All pass, with no change to the existing multisig tests' expectations
- [ ] T040 (partly done 2026-11-25: Send-page rows, row checks, toggle memory, explainer and Spanish checked in the browser preview; review/create/sign/broadcast need a funded testnet shared wallet and two devices, and the 375 px light/dark visual check was not done) Run the [quickstart.md](quickstart.md) walkthrough in the browser preview at 375 px wide, light and dark, English and Spanish, and fix anything that differs from [contracts/ui-contracts.md](contracts/ui-contracts.md)

---

## Dependencies & Execution Order

### Phases

- **Setup (T001)** → **Foundational (T002–T007)** → user stories → **Polish (T036–T040)**.
- Within Foundational: T003 needs T002, T005 needs T004, T007 needs T006. The three pairs run in parallel.

### User stories

- **US1** needs Foundational.
- **US2** needs Foundational. It can be built in parallel with US1: its tests use PSBTs built with the T003 builder and received with `ReceiveProposalAsync`. Its end-to-end check needs US1 to create a proposal in the app. T009 (US1) and T018/T026 (US2) all edit `MultisigWalletService.cs` or `MultisigWalletServiceTests.cs`, so run those one at a time.
- **US3** needs T005 (`RecipientCount`, `ProposalRecipient`); it doesn't depend on US1/US2 code.
- **US4** needs US1 (T011, T012), because it extends the rows and the Send page.

### Within a story

Test task → implementation task → Razor changes. Locale-key tasks ([P]) can go at any time. Razor tasks on the same file run one after another: T012 → T013 → T014 → T034 on `MultisigSend.razor`.

## Parallel Examples

```text
Foundational:  T002 (Crypto tests) | T004 (model tests) | T006 (helper tests)
US1:           T008 (service tests) | T010 (locale keys) | T011 (RecipientRows)
US2:           T015 (reader tests) | T017 (receive tests) | T019 (summary tests) | T021 then T022 (QrCapacity) | T023 (keys)
US3:           T028 (activity tests) | T030 (keys)
Across stories, once Foundational is done: US1, US2 (Wallet parts) and US3 can be worked at the same time.
```

## Implementation Strategy

### MVP

Setup + Foundational + **US1 + US2** (both P1). US1 alone can create proposals, but nobody could check or sign them on another device. Ship the two together, then add US3 and US4 in the same PR or right after.

### Incremental delivery

1. T001–T007, then stop and check the suites are green.
2. US1, then check that creating works (quickstart §1).
3. US2, then check the full sign and broadcast flow (quickstart §3–§5).
4. US3, then US4, then Polish.
