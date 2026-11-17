# Tasks: Multisig Journey & Signing Experience Polish

**Input**: Design documents from `specs/018-multisig-ux-polish/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contracts.md, quickstart.md

**Tests**: Required for every new service in `DigiByte.Wallet` (constitution Principle III: tests written first, must fail before the code exists). UI is verified with the quickstart walkthrough; there is no component-test project.

**Format**: `- [ ] [ID] [P?] [Story?] Description with file path`. `[P]` = different files, no dependency on an unfinished task.

**Scope guard (FR-018)**: presentation only. Do not change `DigiByte.Api`, `DigiByte.P2P.Shared`, `DigiByte.Crypto`, signing/verification, storage formats, or the offline hand-off mechanics.

## Phase 1: Setup

- [X] T001 Confirm baseline: run `dotnet build` and `dotnet test tests/DigiByte.Wallet.Tests` on this branch and note any pre-existing failures
- [X] T002 Create `src/DigiByte.Wallet/Models/MultisigUxModels.cs` with the enums and records from data-model.md: `SignerPresence` (Online, Offline), `SignerStatus` (Waiting, Signed, Declined, Joined, Ready), `MultisigState` (NeedsYourSignature, YouSigned, WaitingForOthers, ReadyToBroadcast, LiveRoom, Sent, CancelledOrExpired, Failed), `PrimaryAction` (None, ReviewAndSign, Broadcast, CreateWallet, OpenRoom, Sign, View, GoToPending), `SignerCardModel`, `RoomHeadline` (Tone: Neutral|Action|Positive|Warning), `PaymentSummaryModel`, `AttentionCount`

## Phase 2: Foundational (blocks all stories)

**Shared vocabulary, status badge and presence logic are used by every story.**

- [X] T003 [P] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxVocabularyTests.cs`: every `MultisigState` has a non-empty label, an icon key and a tone; Sent and CancelledOrExpired have no primary action; NeedsYourSignature maps to Sign; LiveRoom maps to OpenRoom; ReadyToBroadcast maps to Broadcast
- [X] T004 [P] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxPresenceTests.cs`: offline shown only after 10 s offline; returning online is immediate; a brief offline blip under 10 s never shows Offline; unknown key defaults to Offline
- [X] T005 [P] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxPendingStateTests.cs`: classifier returns NeedsYourSignature (AwaitingSignatures and local key not in `SignedBy`), YouSigned (local key in `SignedBy`, below threshold), ReadyToBroadcast (ReadyToFinalize), Sent (Broadcast), CancelledOrExpired (Cancelled), Failed (Failed), LiveRoom when a live room exists and the item is still open; a watch-only viewer (no local key) never gets NeedsYourSignature
- [X] T006 Implement `src/DigiByte.Wallet/Services/MultisigStateVocabulary.cs` (label, icon key, tone, primary action per state) until T003 passes
- [X] T007 Implement `src/DigiByte.Wallet/Services/PresenceDebouncer.cs` (injectable clock, 10 s hold constant) until T004 passes
- [X] T008 Implement `src/DigiByte.Wallet/Services/PendingStateClassifier.cs` using `PendingMultisigTransaction.Status`, `.SignedBy`, `.RequiredSignatures` until T005 passes
- [X] T009 [P] Create `src/DigiByte.Web/Components/Multisig/StatusBadge.razor`: icon + text label from the vocabulary, `aria-label`, never color-only, light and dark classes; also render presence (online/offline dot with text) and signer statuses (Waiting, Signed with check icon, Declined)
- [X] T010 [P] Create `src/DigiByte.Web/Components/Multisig/ConnectionBanner.razor`: "Reconnecting. Your signatures are saved." with `role="status"`, shown from `Room.IsReconnecting`
- [X] T011 [P] Create `src/DigiByte.Web/Components/Multisig/EmptyState.razor`: one sentence plus one optional action button, 44 px minimum touch target
- [X] T012 Add `prefers-reduced-motion` guards for any new animation classes in `src/DigiByte.Web/wwwroot/css/app.css`

**Checkpoint**: vocabulary, presence, classifier and base components exist and tests pass.

## Phase 3: User Story 1 - Signer cards, headline and primary action in the signing room (P1) 🎯 MVP

**Goal**: Each co-signer is a card with presence and status; one headline and one primary button per viewer.

**Independent test**: Quickstart steps 1 and 2 (two or three profiles, presence flip, sign, decline, threshold).

- [X] T013 [P] [US1] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxSignerCardsTests.cs`: initials from one or two words, "?" fallback for empty or emoji-only names; two cards with the same name get a 4-character fingerprint, unique names get none; a signer in `SignedKeys` is Signed even when Offline; `Action` Declined maps to Declined; the viewer's card has `IsYou`; creator flagged
- [X] T014 [P] [US1] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxRoomHeadlineTests.cs` covering every row of the headline table in data-model.md: not signed and below threshold gives ReviewAndSign; signed and below threshold gives "Waiting for N more signature(s)" with no primary action; threshold met gives Broadcast with Positive tone; `Unreachable` gives GoToPending with Warning tone; Broadcast status gives View; watch-only gives "View only" and no action; a signed viewer is never offered ReviewAndSign (FR-004)
- [X] T015 [US1] Implement `src/DigiByte.Wallet/Services/SignerCards.cs` (`ForSigningRoom`) until T013 passes
- [X] T016 [US1] Implement `src/DigiByte.Wallet/Services/SigningRoomHeadline.cs` until T014 passes
- [X] T017 [P] [US1] Create `src/DigiByte.Web/Components/Multisig/SignerCard.razor` and `SignerList.razor`: avatar initials on a key-derived color (decoration only), name with "You" marker, truncating long names, `StatusBadge` for presence and status, creator marker, and an `aria-live="polite"` region announcing changes
- [X] T018 [P] [US1] Create `src/DigiByte.Web/Components/Multisig/RoomHeadlineCard.razor`: title, optional detail, tone styling, exactly one primary button or none
- [X] T019 [US1] Update `src/DigiByte.Web/Components/Multisig/SignerProgress.razor` to show "X of Y signatures", the bar, and a distinct "Ready to broadcast" state; reuse `StatusBadge`
- [X] T020 [US1] Update `src/DigiByte.Web/Pages/MultisigSigningRoom.razor`: feed `PresenceDebouncer` from room updates (re-render on a timer while any card is pending an offline flip), render `SignerList`, `RoomHeadline`, `SignerProgress`, `ConnectionBanner`; make the headline's primary button the only primary action; disable connection-dependent actions with a stated reason when reconnecting; show "view only" for watch-only viewers Also render the Ended phase (expired, closed, broadcast) with the shared vocabulary and one next step
- [X] T021 [US1] Update `src/DigiByte.Web/Components/Multisig/CoSignerRow.razor` to use the same card pieces so wording matches elsewhere

**Checkpoint**: US1 works on its own and delivers the MVP.

## Phase 4: User Story 2 - Clear payment summary before signing (P1)

**Goal**: One consistent summary of what is being signed, in the same order on every screen.

**Independent test**: Quickstart step 3.

- [X] T022 [P] [US2] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxPaymentSummaryTests.cs`: total out equals amount plus fee; address grouped into readable chunks without changing its value; empty memo and no-change rows omitted; field order is fixed (amount, recipient, fee, memo, change, total)
- [X] T023 [US2] Implement `PaymentSummaryModel` builder in `src/DigiByte.Wallet/Services/PaymentSummaryBuilder.cs` from `PendingMultisigTransaction`, taking change and input details from `MultisigProposalReader` (the saved item has no change field); omit the change line when unavailable until T022 passes
- [X] T024 [P] [US2] Create `src/DigiByte.Web/Components/Multisig/PaymentSummary.razor`: prominent amount, full copyable recipient, fee, memo, change note, total, one-line advisory "Check before you sign" cue (no required tick, per FR-007), and a collapsed "Details" expander for inputs and transaction ids
- [X] T025 [US2] Use `PaymentSummary` in `src/DigiByte.Web/Pages/MultisigSigningRoom.razor` and make the sign confirmation restate amount and shortened recipient
- [X] T026 [P] [US2] Use `PaymentSummary` and the restating confirmation in `src/DigiByte.Web/Pages/MultisigPending.razor`
- [ ] T027 [P] [US2] Use `PaymentSummary` in `src/DigiByte.Web/Pages/PsbtSign.razor` (offline sign screen); copy/QR/file hand-off steps unchanged Also apply the shared state vocabulary to its status lines (FR-010) NOT DONE: `PsbtSign.razor` is the generic PSBT tool (any outputs, any wallet), not the multisig offline path; that path is the Pending list, already covered by T026. Left unchanged on purpose.

**Checkpoint**: the same payment reads identically in room, Pending and offline sign screens.

## Phase 5: User Story 3 - Guided creation journey (P2)

**Goal**: Step indicator, one instruction and one action per step, scheme explanation, creation-room cards.

**Independent test**: Quickstart step 4.

- [X] T028 [P] [US3] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxSchemeExplainerTests.cs`: 2 of 3 gives "Any 2 of 3 co-signers must approve each payment"; N of N says all must approve; 1 of N says any one can approve; invalid input (required above total or below 1) returns an empty string
- [X] T029 [P] [US3] Extend `tests/DigiByte.Wallet.Tests/MultisigUxSignerCardsTests.cs` with creation-room cases: Joined vs Ready from `IsReady`, initiator flagged, no debouncer (a joined co-signer is online); missing co-signers (total minus joined) appear as "Not joined yet" slots
- [X] T030 [US3] Implement `src/DigiByte.Wallet/Services/SchemeExplainer.cs` until T028 passes
- [X] T031 [US3] Add `ForCreationRoom` to `src/DigiByte.Wallet/Services/SignerCards.cs` until T029 passes (no presence handling for creation rooms)
- [X] T032 [P] [US3] Create `src/DigiByte.Web/Components/Multisig/StepIndicator.razor` (Set up, Invite, Confirm, Ready; numbered text plus state, not color-only)
- [X] T033 [US3] Update `src/DigiByte.Web/Pages/MultisigRoomPage.razor`: `StepIndicator`, `SignerList` with Joined/Ready state plus "Not joined yet" slots (no presence), `RoomHeadline`-style waiting message ("Waiting for 1 more co-signer to join", "Everyone is ready: create wallet"), clear Share action for the code, link and QR, and the final Create wallet button with positive tone
- [X] T034 [US3] Update `src/DigiByte.Web/Pages/MultisigCreate.razor`: step indicator, live scheme explanation next to the M-of-N selects, one orienting sentence and one primary action per step
- [ ] T035 [P] [US3] Update `src/DigiByte.Web/Pages/MultisigImport.razor` to use the same step language and CTA conventions Also apply the shared state vocabulary to its status lines (FR-010) NOT DONE: Import is a single form, not a stepped flow, so a step indicator would add noise. Left unchanged.

## Phase 6: User Story 4 - Consistent status, icons and CTAs across lists and wallet cards (P2)

**Goal**: Same label, icon and primary action everywhere; attention badges.

**Independent test**: Quickstart step 5.

- [X] T036 [P] [US4] Write failing tests `tests/DigiByte.Wallet.Tests/MultisigUxAttentionTests.cs`: counts only AwaitingSignatures items whose local key is not in `SignedBy`; per-wallet counts and total are correct; wallets without a local key count 0; empty input gives zero total
- [X] T037 [US4] Implement `src/DigiByte.Wallet/Services/AttentionCounter.cs` until T036 passes
- [X] T038 [US4] Check that pending items are readable for a locked wallet (see research 3b): read how `MultisigWalletService` loads `multisig_pending_` items, and record the result as a comment on T039. If they are not readable while locked, show counts for unlocked wallets only and add a short "locked" hint
- [X] T039 [P] [US4] Create `src/DigiByte.Web/Components/Multisig/AttentionBadge.razor` (hidden at 0, text plus icon, accessible name "N payments need your signature")
- [X] T040 [US4] Update `src/DigiByte.Web/Pages/MultisigWallets.razor` and `src/DigiByte.Web/Components/Multisig/WalletChip.razor` to show per-wallet badges, and the multisig home total in `src/DigiByte.Web/Pages/MultisigDetail.razor` or the multisig entry page
- [X] T041 [US4] Update `src/DigiByte.Web/Pages/MultisigPending.razor` to render each item's state via `PendingStateClassifier` and `StatusBadge`, with one primary button per state (Sign, Open room, Broadcast, View), "You signed" with a check and how many more are needed, and a live-room indicator
- [X] T042 [P] [US4] Update `src/DigiByte.Web/Components/Multisig/ActivityItem.razor` and `src/DigiByte.Web/Pages/MultisigActivity.razor` to use the shared vocabulary for Sent, Cancelled/Expired and Failed

## Phase 7: User Story 5 - Calm instructions, empty states and feedback (P3)

**Independent test**: Quickstart step 6.

- [ ] T044 [P] [US5] Audit each multisig page (`Multisig*.razor`, `PsbtSign.razor`) for instructional text: keep one short orienting sentence, move the rest into the existing `CollapsePanel.razor` "How it works" expander, link to `MultisigGuide.razor` NOT DONE: no page-by-page text audit yet.
- [ ] T045 [P] [US5] Use `EmptyState` for empty Pending and Activity lists and for a room with no co-signers yet PARTIAL: Pending list uses `EmptyState`; Activity and the empty room are not changed.
- [ ] T046 [P] [US5] Add consistent success confirmations (signature added, threshold reached, wallet created, payment sent) via the app's existing toast or notice pattern, with motion skipped under reduced motion PARTIAL: signing room confirms "Your signature was added."; other pages keep their existing confirmations.
- [ ] T047 [US5] Check phone width (375 px) on every multisig screen: primary action reachable without horizontal scrolling, 44 px touch targets, 7-of-7 list stays scannable, long and non-Latin names truncate NOT DONE: phone-width pass not run.

## Phase 8: Polish & cross-cutting

- [X] T048 Regenerate `src/DigiByte.Web/wwwroot/css/tailwind.css` with the existing Tailwind build (as done in commit `e0bcf2d` for 017) so new classes are present
- [ ] T049 Run the full quickstart (`specs/018-multisig-ux-polish/quickstart.md`) including light/dark, reduced motion, keyboard-only and a screen reader pass on status changes PARTIAL: creation room checked in a browser (step indicator, scheme text, invite, signer cards, waiting headline); signing room, Pending, wallets list, dark/light, reduced motion, keyboard and screen reader not checked in a browser.
- [ ] T050 Regression: offline share/import end to end, and a signing room ending still saves collected signatures; run `dotnet test` for the whole solution PARTIAL: Wallet tests pass (2269) except 6 live-network explorer benchmarks that need internet; offline share/import not run end to end.
- [X] T051 Confirm FR-018: `git diff --stat` shows no changes under `src/DigiByte.Api`, `src/DigiByte.P2P.Shared`, `src/DigiByte.Crypto`, `src/DigiByte.NodeApi`
- [ ] T052 Update `specs/018-multisig-ux-polish/checklists/requirements.md` notes and, if the project keeps one, the changelog or docs entry for the new multisig experience NOT DONE.

## Dependencies & Execution Order

- Setup (P1) then Foundational (P2) block everything.
- **US1 and US2** (both P1) can run in parallel after Foundational; they touch `MultisigSigningRoom.razor` in different tasks (T020 then T025), so do T020 before T025.
- **US3** needs `SignerCard`/`SignerList` from T017 and `SignerCards` from T015 (T031 extends T015's file, so T015 first).
- **US4** needs the Foundational classifier and `StatusBadge`; T041 and T026 both edit `MultisigPending.razor`, so do T026 before T041. T027 and T035 also apply the shared vocabulary on `PsbtSign.razor` and `MultisigImport.razor` (T043 was merged into them, so the numbering skips it).
- **US5** runs last over finished pages.
- Polish after all stories.

## Parallel Example

- Foundational tests T003, T004, T005 together; then components T009, T010, T011 together.
- After Foundational: US1 tests T013, T014 together; US2 test T022 and components T024 in parallel with US1 work.
- US3 T028, T029, T032 together; US4 T036, T039 together.

## Implementation Strategy

1. **MVP**: Setup, Foundational, and US1 (signer cards, headline, primary action in the signing room). Validate with quickstart steps 1 and 2.
2. Add US2 (payment summary) for the safety-relevant clarity, then US3 and US4 in either order.
3. Finish with US5 and Polish. Each story is independently demonstrable, so stop after any checkpoint if needed.
