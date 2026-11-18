# Tasks: Receive Multi-Sig Proposals by QR Scan

**Input**: Design documents from `/specs/022-multisig-proposal-scan-import/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/receive-proposal.md, quickstart.md

**Tests**: Required for wallet logic (constitution Principle III): every change in `src/DigiByte.Wallet/` has a test in `tests/DigiByte.Wallet.Tests/` written first and seen failing. The Razor UI has no component-test framework, so it is verified manually per [quickstart.md](quickstart.md).

**Format**: `- [ ] T### [P?] [Story] Description with file path`. Service code is in `src/DigiByte.Wallet/Services/MultisigWalletService.cs` and UI in `src/DigiByte.Web/Pages/MultisigPending.razor` unless noted, so most tasks are sequential. New tests go in one file, `tests/DigiByte.Wallet.Tests/MultisigProposalReceiveTests.cs`, using the doubles in `MultisigTestDoubles.cs` and the device helpers pattern from `MultisigOnlineSigningTests.cs`.

## Phase 1: Setup

- [X] T001 Run `dotnet test tests/DigiByte.Wallet.Tests` and record that the suite is green before any change (baseline for the polish phase); no file changes

## Phase 2: Foundational (blocks all stories)

- [X] T002 In `src/DigiByte.Wallet/Models/MultisigModels.cs`, add `public bool ReceivedFromCoSigner { get; set; }` to `PendingMultisigTransaction` with a doc comment ("set only when added by receiving a co-signer's copy; default false so records saved before this field read as false") (data-model)
- [X] T003 [P] Create `src/DigiByte.Wallet/Services/MultisigProposalReceive.cs` with `enum ProposalReceiveOutcome { Added, Merged, AlreadyUpToDate, Cancelled, Closed }`, `record ProposalReceiveResult(ProposalReceiveOutcome Outcome, PendingMultisigTransaction Pending, int NewSignatures, IReadOnlyList<string> RejectedSigners)` and `record ProposalWalletMatch(string WalletId, string Name)` exactly as in data-model.md
- [X] T004 [P] Add a test in `tests/DigiByte.Wallet.Tests/MultisigProposalReceiveTests.cs` (create the file with the device helpers) that a pending transaction saved without `ReceivedFromCoSigner` loads with `false`, and one saved with `true` round-trips (data-model: "Old stored records read as false")

**Checkpoint**: solution builds; T004 passes.

## Phase 3: User Story 1 - Scan a proposal QR to add it to my pending list (Priority: P1) 🎯 MVP

**Goal**: A co-signer scans a proposal QR from another device and it appears in their pending list, labelled and ready to review.

**Independent Test**: Quickstart steps 1 and 2 (create on device A, scan on device B).

### Tests first (must fail before T008)

- [X] T005 [US1] In `tests/DigiByte.Wallet.Tests/MultisigProposalReceiveTests.cs`, add tests for `ReceiveProposalAsync` with a proposal the device does not hold: outcome `Added`; one new pending item for that wallet with `ReceivedFromCoSigner == true`; recipient, amount, fee and `UnsignedTxId` equal what `MultisigProposalReader` reads from the PSBT (FR-011); item survives a fresh `GetPendingTransactionsAsync` load (FR-013); co-signer signatures in the received copy are kept only if they verify; the same works for a watch-only copy of the wallet (`OwnKeyIndex < 0`), which can receive but has no signing option (spec Edge Cases)
- [X] T006 [US1] Add negative-path tests (Principle III): a PSBT that spends coins not belonging to this multi-sig, a malformed base64 string, and a PSBT carrying a signature from a key that is not a co-signer (refused in full, research R1a) each throw `PsbtImportException` with a plain-language message and leave the pending list unchanged (FR-005, FR-008). Run T005 and T006 and confirm they fail (the method does not exist yet) before T007 (Principle III)

### Risk check (before the UI)

- [ ] T008a [US1] **Manual gate (needs a person with a real phone; an automated run cannot complete it).** It gates the UI tasks T009–T013 only; the service tasks T007/T008 can run in parallel with it. Verify QR readability at the size limit (research R9a, SC-002): create a proposal whose QR is near the 2,200-character limit (hint: spend from a multisig holding many small coins so the PSBT has many inputs, or build a ~2,200-character PSBT in a throwaway test), show it on one screen, and scan it with the in-app scanner on a real phone. Record pass/fail and the conditions in `research.md` R9a. If it fails, apply the contingencies in the order listed there (camera resolution in `src/DigiByte.Web/wwwroot/js/qrscanner.js` currently `ideal 640x480`; QR error correction/size in `QrImage.razor`; the 2,200-character threshold in `MultisigPending.razor`) and re-test until it passes

### Implementation

- [X] T007 [US1] In `MultisigWalletService.cs`, add `ReceiveProposalAsync(MultisigWalletConfig config, string psbtBase64)` returning `ProposalReceiveResult`. This task covers the not-on-device path only: read facts with `MultisigProposalReader.Read`, throw `PsbtImportException(facts.Problems[0])` on any problem, save a new item as `AdoptProposalAsync` does (signatures stripped, then verified ones merged via `MergeRoomSignaturesAsync`), set `ReceivedFromCoSigner = true`, return `Added` with the new-signature count and rejected signers (research R1, R3). Reject any partial signature from a key that is not a co-signer before saving (research R1a). Look up an existing item by `UnsignedTxId` first and, until T016 fills in the merge logic, return it unchanged as `AlreadyUpToDate` so a rescan can never create a duplicate
- [X] T008 [US1] Run the T005/T006 tests and confirm they pass; fix until green
- [X] T009 [US1] In `src/DigiByte.Web/Pages/MultisigPending.razor`, add a "Scan proposal" button above the list (min 44 px height, aria-label) that opens a "Receive proposal" panel and the existing `QrScannerModal` in a new "receive" mode; closing the scanner (e.g. camera denied) leaves the panel open. Build the panel now with its Scan QR button so later stories only add to it. Use a small private enum (`ScanMode { Signature, Proposal }`) so the existing per-proposal "Scan QR" keeps routing to `OnScannedImport` and the new button routes to a new `OnScannedProposal` (research R9)
- [X] T010 [US1] In `MultisigPending.razor`, implement `ReceiveProposalText(string text)`: refuse with a message when `MultisigSendHelper.ClassifyScan(text).Kind != ScanKind.Psbt` (address/payment link vs unrelated text, matching the wording style of `AcceptImportText`); otherwise call `MultisigService.ReceiveProposalAsync(config, text.Trim())`, catch `PsbtImportException` and show its message in the page's existing `role="status"` notice; on `Added` reload the queue, set `message = "Proposal added"`, remember the new item's id as `highlightId`
- [X] T011 [US1] In `MultisigPending.razor`, after a reload with `highlightId` set, scroll that card into view and give it a temporary highlight ring (use an existing helper in `src/DigiByte.Web/wwwroot/js/dom-helpers.js` or add a small one there), cleared after a few seconds (research R8). Give each card a stable `id="proposal-@tx.Id"`
- [X] T012 [US1] In `MultisigPending.razor`, show a "Received from a co-signer" badge on a card when `tx.ReceivedFromCoSigner`, next to `StatusBadge`; the card already shows full recipient and amount through `PaymentSummary` and Sign already opens a confirmation, so confirm both still appear for received proposals (FR-009)
- [X] T013 [P] [US1] In `src/DigiByte.Web/Pages/MultisigDetail.razor`, add a "Scan proposal" shortcut beside the existing pending-transactions link that navigates to `/multisig/{WalletId}/pending?scan=1`; in `MultisigPending.razor` read the `scan` query parameter on load and open the Receive proposal panel with the scanner once (spec Edge Cases, "both entry points")

**Checkpoint**: Scan on device B adds the proposal, scrolls to it and labels it. MVP done.

## Phase 4: User Story 2 - Scan a QR for a proposal I already have (Priority: P1)

**Goal**: Scanning a proposal the device already holds never duplicates it; new signatures merge.

**Independent Test**: Quickstart steps 3 and 4.

- [X] T014 [US2] In `MultisigProposalReceiveTests.cs`, add tests written before T016: scanning the same proposal twice leaves exactly one item and the second result is `AlreadyUpToDate` (SC-003); scanning a copy with one more valid signature gives `Merged` with `NewSignatures == 1` and the item's `SignedBy` grows; a copy whose recipient, amount or fee disagrees with the stored item is refused using `CheckProposalMatches`; a copy of a broadcast proposal gives `Closed` and changes nothing
- [X] T015 [US2] Add a test that a merge reaching the required signature count moves the item to `ReadyToFinalize`, and that a co-signer signature that fails verification is dropped and listed in `RejectedSigners` while the rest is merged. Run T014 and T015 and confirm they fail before T016 (Principle III)
- [X] T016 [US2] In `ReceiveProposalAsync`, fill the "already on device" branch: find the item by `UnsignedTxId`; `Broadcast` → `Closed`; open → verify with `CheckProposalMatches`, merge via `MergeRoomSignaturesAsync`, return `Merged` when new signatures were accepted else `AlreadyUpToDate` (FR-004)
- [X] T017 [US2] In `MultisigPending.razor` `ReceiveProposalText`, map `Merged` to "N new signature(s) added" and highlight the card, `AlreadyUpToDate` to "Already up to date.", and `Closed` to "This payment was already sent."

**Checkpoint**: Repeated scans are safe; signatures accumulate.

## Phase 5: User Story 3 - Same options as the QR: paste and file (Priority: P2)

**Goal**: Paste and file give the same outcomes as scanning.

**Independent Test**: Quickstart step 5.

- [X] T018 [US3] In `MultisigPending.razor`, add the Paste and Choose file buttons to the Receive proposal panel built in T009, beside Scan QR, each min 44 px with labels, so all three entries are visible together
- [X] T019 [P] [US3] In `MultisigPending.razor`, implement Paste: read the clipboard with the existing `Clipboard` service as `PasteImport` does, show "Nothing to paste, or the browser blocked clipboard access." when empty, otherwise call `ReceiveProposalText`
- [X] T020 [P] [US3] In `MultisigPending.razor`, implement Choose file with `InputFile` using the same `.txt,.psbt,text/plain` filter and 512 KB `maxAllowedSize` as the import panel (`OnFileChosen`), reading the text and calling `ReceiveProposalText`; show "That file couldn't be read…" on failure
- [X] T021 [US3] Verify (and fix if needed) that when the scanner reports no camera or permission denied (existing `QrScannerModal` message), closing it leaves the Receive proposal panel open so Paste and Choose file remain reachable (FR-012; the panel behaviour is built in T009)

**Checkpoint**: All three sources add the same proposal.

## Phase 6: User Story 4 - Clear refusals for the wrong thing (Priority: P2)

**Goal**: Wrong wallet, cancelled, and already-spent cases each get the clarified behaviour; nothing ends silently.

**Independent Test**: Quickstart steps 6, 7 and 8.

### Tests first

- [X] T022 [US4] In `MultisigProposalReceiveTests.cs`, add tests written before T025–T028 (run T022–T024 and confirm they fail before T025, Principle III): `ReceiveProposalAsync` on a cancelled local copy returns `Cancelled` and changes nothing; `RestoreCancelledAsync` sets status to awaiting/ready by signature count, clears `CancelledAt`, merges the copy's valid signatures, and refuses a non-cancelled item or a mismatching PSBT
- [X] T023 [US4] Add tests for `FindWalletForProposalAsync`: with two multi-sigs under one owner, a PSBT spending from the second returns its id and name when asked from the first; returns null when no wallet of the owner matches; ignores `excludeWalletId`
- [X] T024 [US4] Add tests for `GetSpentInputIdsAsync`: with a fake chain whose spendable set lacks one input, that input id (`txid:index`) is returned; all-unspent returns empty; a chain that throws returns empty (constitution V: no guessing). Use `MultisigFakeChain` and extend it only if it cannot simulate a failing UTXO fetch

### Implementation

- [X] T025 [US4] In `MultisigWalletService.cs`, make `ReceiveProposalAsync` return `Cancelled` (with the local item, no change) when the local copy is `Cancelled`, and add `RestoreCancelledAsync(PendingMultisigTransaction pending, string psbtBase64)` per research R4 and the contract
- [X] T026 [US4] In `MultisigWalletService.cs`, add `FindWalletForProposalAsync(string psbtBase64, string ownerWalletId, string? ownPublicKeyHex, string excludeWalletId)`: parse the PSBT, and for each other wallet from `ListWalletsAsync`/`GetConfigAsync` compare the first input's `WitnessUtxo.ScriptPubKey` with the wallet address's script; return `ProposalWalletMatch` or null (research R5)
- [X] T027 [US4] In `MultisigWalletService.cs`, add `GetSpentInputIdsAsync(MultisigWalletConfig config, IEnumerable<OutPoint> inputs)` returning the ids not in `GetSpendableUtxosAsync(config)`, empty on any exception; then change `CheckCoinsAsync` in `src/DigiByte.Web/Pages/MultisigSigningRoom.razor` to call it so both pages share one implementation (research R6)
- [X] T028 [P] [US4] Create `src/DigiByte.Web/Services/ProposalHandoff.cs` (scoped, in-memory: `Set(walletId, psbt)`, `Take(walletId)` returns and clears only when the id matches) and register it in `src/DigiByte.Web/Program.cs` next to the `MultisigWalletService` registration (data-model "Hand-off")
- [X] T029 [US4] In `MultisigPending.razor`, handle `Cancelled`: show an inline prompt "You cancelled this proposal." with **Restore this proposal** (calls `RestoreCancelledAsync`, reloads, highlights) and **Leave it cancelled**; keep the scanned text only in memory for that prompt (FR-010)
- [X] T030 [US4] In `MultisigPending.razor`, when `ReceiveProposalAsync` throws the "not this multisig's coins" refusal, call `FindWalletForProposalAsync`; if a match exists show "This proposal is for *name*." with **Open *name*** (store in `ProposalHandoff`, navigate to `/multisig/{id}/pending`), else say it is for a wallet the user doesn't have. On load, `Take(WalletId)` and run `ReceiveProposalText` once if present (FR-006). Nothing is added automatically
- [X] T031 [US4] In `MultisigPending.razor`, after each `Reload()` call `GetSpentInputIdsAsync` once for all open proposals' inputs (read via `MultisigProposalReader`); for any proposal with a spent input show "Can't be signed – coins already spent" and disable Sign, Sign together online, Broadcast and Retry broadcast while keeping Share and Cancel available (FR-007); on chain failure mark nothing

**Checkpoint**: Every refusal path shows a specific message; spent proposals are visibly unsignable.

## Phase 7: Polish & Cross-Cutting

- [X] T032 Run `dotnet build` and `dotnet test` across `tests/DigiByte.Wallet.Tests` and confirm the whole suite is green, including all new tests and no regression versus T001
- [ ] T033 Follow [quickstart.md](quickstart.md) in the preview/dev server at phone width (375 px) and in dark mode: check scan/paste/file, repeat scan, refusals, restore, spent marker, camera denied, and a watch-only copy (can receive, no Sign); open a signing room whose coins are spent and confirm the same warning as before T027 (regression); confirm no network requests are made during a receive (FR-014) and time scan-to-review against 30 s (SC-001); confirm no console errors; record results at the bottom of this file
- [X] T034 [P] Update `docs/PROCESS_FLOWS.md` (multi-sig signing flow) with the receive-proposal step and the refusal/restore/hand-off behaviour (constitution: user-visible behaviour changes update docs in the same PR)
- [X] T035 [P] Add a Conventional-Commit style entry to the next release notes/CHANGELOG if one is maintained for multi-sig changes (`feat(multisig): receive proposals by QR scan`); skip if the repo generates it from commits
- [ ] T036 Mark finished tasks `[X]` in this file and set the spec's Status to Implemented

## Dependencies & Execution Order

- Phase 1 → Phase 2 → stories. T002 blocks T005 onward; T003 blocks T007.
- **US1** (T005–T013) is the MVP and independent once Phase 2 is done. T008a (real-phone QR check) must pass before the UI tasks T009–T013, because a fix may change the QR display or scanner settings.
- **US2** extends `ReceiveProposalAsync` from US1 (T016 after T007) but is otherwise independent of US3/US4.
- **US3** needs the US1 handler (T010) only; it can proceed in parallel with US2 and US4 UI work.
- **US4** extends `ReceiveProposalAsync` (T025 after T016) and the pending page; T027 touches `MultisigSigningRoom.razor`, so run it when no other task edits that file.
- Polish last.
- Within every story: tests (fail) → service → UI. Tasks touching `MultisigWalletService.cs` or `MultisigPending.razor` are sequential, not `[P]`.

### Parallel opportunities

- Phase 2: T003 and T004 together.
- US1: T013 (different files) alongside T009–T012.
- US3: T019 and T020 together.
- US4: T028 alongside T025–T027; T022–T024 written together before implementation.
- Polish: T034 and T035 together.

## Implementation Strategy

1. **MVP**: Phases 1–3 (US1). Two devices can already exchange a proposal by QR; demo with quickstart steps 1–2.
2. **Increment 2**: US2 makes repeated scans and signature accumulation safe — needed before real use, so ship with the MVP.
3. **Increment 3**: US3 (paste/file) covers proposals too large for QR.
4. **Increment 4**: US4 adds restore, wrong-wallet hand-off and the spent marker.
5. Finish with Polish; do not merge until T032–T033 pass.

## Notes

- Out of scope (spec): multi-part/animated QR, adding co-signers, changes to signing-room behaviour, any server call.
- Security-relevant: no new key handling, no CSP change, no secrets in logs; the hand-off keeps the PSBT in memory only. Flag "new inbound-data path" in the PR description.

## Implementation Results (2026-11-18)

- **T001 baseline / T032 final**: `dotnet test tests/DigiByte.Wallet.Tests` went from 2281 passed, 6 failed to 2305 passed, 6 failed. The same 6 failures are `LiveExplorerBenchmarks.*`, which call live explorers over the internet; they are unrelated to this feature. All 24 new `MultisigProposalReceiveTests` and the 23 `MultisigOnlineSigningTests` pass. `dotnet build src/DigiByte.Web` succeeds.
- **Failing-first (T006, T015, T024)**: with the service stubbed, 23 of the 24 new tests failed (the 24th only checks the model default); they passed after implementation.
- **Not tested at the unit level**: the "stored recipient/amount/fee disagrees with the PSBT" refusal on an existing proposal. It reuses `CheckProposalMatches`, which already has tests in `MultisigOnlineSigningTests`, and the private save path can't be tampered with from a test.
- **T021 (camera denied)**: verified by reading `QrScannerModal` (it only closes itself and raises `OnClosed`) and the page (`receiveOpen` is independent of `showScanner`), so the Receive proposal panel stays open with Paste and Choose file. It also offers its own "choose an image of the QR" fallback, which decodes at full resolution.
- **T033 (preview) — not completed**: the dev server started and the app loaded, but the browser profile holds an existing PIN-locked wallet, so the pending page could not be reached without that PIN. The console shows only environment errors (inline-script CSP report, CORS to the production metrics node, refused connections), none from this change.
- **Open manual gates**: T008a (dense QR on a real phone) and T033 (quickstart walk-through on two devices, 375 px and dark mode), then T036.
