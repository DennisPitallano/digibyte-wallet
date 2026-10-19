# Tasks: Baku Mascot — Guide & Guardian

**Input**: Design documents from `/specs/002-baku-mascot-guide/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contracts.md, quickstart.md

**Tests**: Included only for `SendRiskEvaluator` (wallet-layer logic), because the constitution (Principle III) requires tests for `DigiByte.Wallet` changes. UI is verified through quickstart.md.

**Organization**: Grouped by user story so each can be built and verified independently.

## Format: `- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]**: can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: US1 to US4 (user story phases only)

Paths: `W` = `src/DigiByte.Web`, `L` = `src/DigiByte.Wallet`, `T` = `tests/DigiByte.Wallet.Tests`.

---

## Phase 1: Setup (Mascot Assets)

- [X] T001 Write a one-off local script (outside the shipped build, e.g. `tools/mascot-slice/slice.py`) that chroma-keys the green background and crops each labelled pose from the three sheets in `W/wwwroot/images/mascot/` into individual transparent images
- [X] T002 Run the script to generate WebP poses at roughly 256 px and 512 px into `W/wwwroot/images/mascot/poses/` using the slugs in data-model.md (greeting, welcome, protective, guard, celebrating, thinking, idea, proud, grateful, approve, wallet, holding-dgb, and the send/receive poses); visually check green-edge fringing on wings and glow effects
- [X] T003 Verify total pose payload is about 300 KB or less and each displayed pose about 15 KB or less; recompress if needed
- [X] T004 Exclude the original three JPEG sheets from published and precached output in `W/DigiByte.Web.csproj` (keep them in the repo as source art)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core Baku infrastructure used by every story. No story work starts before this is done.

- [X] T005 [P] Create `BakuPose`, `BakuMessage` (Id, Severity Info/Tip/Warning/Critical, PoseId, Title, Body, Dismissible, RequiresConfirmation) and `BakuMode` (Full, GuardianOnly, Off) types in `W/Services/BakuCatalog.cs`
- [X] T006 Add the English message catalog and pose table to `W/Services/BakuCatalog.cs`, using the ids, severities, poses and copy in data-model.md "Message Catalog"; Warning and Critical entries must have Dismissible = false
- [X] T007 Write failing tests in `tests/DigiByte.Wallet.Tests/BakuServiceTests.cs` (this project already references `DigiByte.Web` and has a `FakeJSRuntime`, as used by `AppStateTests.cs`): priority Critical > Warning > Tip > Info; ties go to the most recent; Info and Tip filtered under GuardianOnly and Off while Warning and Critical are never filtered; dismissed tips ignored; DismissAsync is a no-op for Warning and Critical; ResetTipsAsync clears dismissals; mode and dismissals round-trip through `dgb-baku-mode` and `dgb-baku-dismissed`; corrupt or missing stored values fall back to Full with none dismissed
- [X] T008 Implement `BakuService` in `W/Services/BakuService.cs` per contracts/ui-contracts.md: Mode/SetModeAsync, Request/Clear, Current (single message, priority Critical > Warning > Tip > Info, ties to most recent, mode filtering where only Info and Tip are filtered and Warning and Critical are never filtered), DismissAsync (no-op for Warning/Critical), ResetTipsAsync, ConfirmAsync, OnChanged; make the T007 tests pass; make the T007 tests pass
- [X] T009 Persist preferences in `BakuService` using `localStorage` keys `dgb-baku-mode` and `dgb-baku-dismissed` (comma-separated IDs), with load on startup and try/catch like `W/Services/AppState.cs`; store nothing else
- [X] T010 Register `BakuService` as scoped in `W/Program.cs` and load its preferences at app startup alongside `AppState`
- [X] T011 [P] Create `W/Components/Shared/BakuMessage.razor`: pose image with empty alt, title, body, dismiss control for dismissible messages, text-only fallback when mode is Off or the image `onerror` fires, reserved space (never overlaying inputs or buttons), polite live region (assertive for Critical)
- [X] T012 [P] Add Baku styles to `W/wwwroot/css/app.css` (and the Tailwind input if needed): responsive pose size (small on narrow screens), CSS-only entrance transition and a short celebration effect, no idle loop, all motion disabled under `prefers-reduced-motion` (FR-013)
- [X] T013 [P] Create `W/Components/Shared/BakuWarningDialog.razor` with "Go back" and "I understand, continue"; Escape and back count as cancel; always shows text; wire it to `BakuService.ConfirmAsync`
- [X] T014 Host the dialog and shared Baku outlet once in the app layout (find the layout under `W/Layout/`) so pages only call the service

**Checkpoint**: Baku can show any catalog message on any page.

---

## Phase 3: User Story 1 - Baku welcomes and guides new users (Priority: P1) 🎯 MVP

**Goal**: Greeting and step guidance through onboarding, with celebration on success.

**Independent Test**: With no wallet, walk create and backup. Baku shows on each step with the right pose, and every step completes with Baku hidden.

- [X] T015 [P] [US1] Add `BakuMessage` greeting to `W/Pages/Welcome.razor` (greeting pose, short welcome)
- [X] T016 [P] [US1] Add step guidance to `W/Pages/CreateWallet.razor`
- [X] T017 [P] [US1] Add step guidance to `W/Pages/RecoverWallet.razor`
- [X] T018 [P] [US1] Add why-backup guidance to the seed display step in `W/Pages/BackupSeed.razor` (never read or render seed content in Baku code)
- [X] T019 [US1] Show the celebrating pose on wallet-created and backup-confirmed success states in `W/Pages/CreateWallet.razor` and `W/Pages/BackupSeed.razor`
- [X] T020 [P] [US1] Add the small greeting to `W/Pages/Unlock.razor`, hidden once password entry begins and never obstructing the password field (FR-002a)
- [X] T021 [US1] Verify US1 via quickstart.md scenarios 1 and 9, including the Baku Off run

**Checkpoint**: Onboarding guide is shippable on its own (MVP).

---

## Phase 4: User Story 2 - Baku guards against risky actions (Priority: P1)

**Goal**: Confirm-to-proceed warnings for seed reveal, risky sends and P2P.

**Independent Test**: Trigger each risk and confirm the warning appears and the action proceeds after confirmation.

### Tests first (constitution Principle III)

- [X] T022 [P] [US2] Write failing tests in `T/SendRiskEvaluatorTests.cs`: amount excludes fee and send-max is 100% (true); `LargeAmount` at 49.99% (false), 50% (true), above 50% (true), zero balance (false, no divide-by-zero); `NewAddress` for unknown (true), saved contact (false), previously sent (false), empty or whitespace destination; DGB and DigiDollar parity

### Implementation

- [X] T023 [US2] Implement `SendRiskEvaluator.Evaluate(amount, balance, destination, knownAddresses)` returning `SendRiskAssessment { LargeAmount, NewAddress }` in `L/Services/SendRiskEvaluator.cs`; pure, no I/O, no address validation; make T022 pass
- [X] T024 [US2] Resolve the open item from research.md: determine where previously-sent addresses can be derived for DGB and DigiDollar from existing transaction history, and add `L/Services/KnownAddressProvider.cs` that builds the known-address set from saved contacts (`L/Services/ContactService.cs`) plus previously-sent addresses, using whichever history source `WalletService` and `DigiDollarService` already expose for DGB and DigiDollar respectively; derive from existing history only, no new storage or secrets; add unit tests in `T/KnownAddressProviderTests.cs`
- [X] T025 [P] [US2] Add the critical reveal warning (RequiresConfirmation) before the seed phrase is shown or exported in `W/Pages/BackupSeed.razor` (seed.reveal); `ExportXpub.razor` is out of scope because it exports a public key, not a secret
- [X] T026 [US2] Wire `SendRiskEvaluator` into `W/Pages/Send.razor` review step, evaluating only after client-side address validation has passed and measuring the amount excluding the network fee (send-max counts as 100%): warn on `NewAddress` and `LargeAmount`, require confirmation, then proceed unchanged
- [X] T027 [US2] Wire the same evaluator and warnings into `W/Pages/DigiDollarSend.razor`, again only after address validation
- [X] T028 [P] [US2] Add the verify-counterparty and scam warning to `W/Pages/P2PMarketplace.razor` (FR-006a)
- [X] T029 [US2] When both `NewAddress` and `LargeAmount` apply, show one combined confirmation using the `send.new-and-large` message (single dialog, one confirmation), per FR-016
- [X] T030 [US2] Verify US2 via quickstart.md scenarios 2 to 4, and the image-blocked check for text fallback

**Checkpoint**: Guardian protections work independently of tips.

---

## Phase 5: User Story 3 - Contextual tips and feedback (Priority: P2)

**Goal**: Tips, empty states, error empathy and success feedback with dismissal.

**Independent Test**: Visit the listed screens with funded and empty wallets, and dismissing a tip keeps it hidden.

- [X] T031 [P] [US3] Add empty-state message and a tip to `W/Pages/Home.razor`
- [X] T032 [P] [US3] Add tip to `W/Pages/Receive.razor`
- [X] T033 [P] [US3] Add tip to `W/Pages/DigiDollar.razor`
- [X] T034 [P] [US3] Add tip to `W/Pages/Settings.razor`
- [X] T035 [US3] Show the thinking pose beside friendly error text for failed or rejected actions on `W/Pages/Send.razor` and `W/Pages/DigiDollarSend.razor`
- [X] T036 [US3] Show approve or celebrate feedback after a completed send, hooking into the existing `W/Components/Shared/TxSuccessOverlay.razor` flow without altering its behavior
- [X] T037 [P] [US3] Integrate Baku in the Help Center `W/Pages/Help.razor`: `help.intro` header above the search box and topic accordion, and `help.no-results` (thinking pose) when the search filter returns no sections; topics and search behave exactly as before and Baku never overlays them
- [X] T038 [US3] Add the "Reset tips" action to `W/Pages/Settings.razor`, calling `BakuService.ResetTipsAsync` (FR-009)
- [X] T039 [US3] Verify US3 via quickstart.md scenarios 6 and 7, including the Help Center intro and the empty-search state

---

## Phase 6: User Story 4 - User controls Baku's presence (Priority: P3)

**Goal**: Mode selector in Settings.

**Independent Test**: Switch each mode and confirm the right appearances show and persist after reload.

- [X] T040 [US4] Add a presence selector (Full, Guardian only, Off, where Off hides Info and Tip only) to `W/Pages/Settings.razor`, persisted via `BakuService.SetModeAsync`
- [X] T041 [US4] Confirm Guardian-only hides tips and info but keeps warnings, and Off hides Info and Tip but still shows Warning and Critical text without the image
- [X] T042 [US4] Verify US4 via quickstart.md scenario 8 and the under-15-seconds change check (SC-007)

---

## Phase 7: Polish & Cross-Cutting

- [X] T043 [P] Responsive check at about 360 px, tablet and desktop: Baku never covers balances, addresses, amounts or primary buttons (FR-012, SC-006)
- [X] T044 [P] Accessibility pass: screen-reader announcements, contrast, reduced-motion disables all effects (FR-013) — checked: Baku cards use role status/alert with polite/assertive live regions, decorative images have empty alt, the dialog is aria-modal with labelled title/body and focuses Go back, Escape cancels, text contrast is 7.3:1 or better in dark and light, and reduced-motion rules disable the Baku animations. Not done: a real screen-reader pass (none available here).
- [X] T045 [P] Offline check: confirm poses are precached by the service worker and display offline (research.md section 8) — found and fixed: .webp was missing from the service worker's precache include list, so no pose was cached; now all 15 are. Verified by inspecting the published manifest and simulating the filter. Not run in a browser: the built-in preview pane cannot register service workers.
- [X] T046 [P] Performance check: first-load size and time versus baseline, at most 5% increase (SC-005) — measured on a release publish: 6.36 MB download; Welcome first load adds 13.5 KB (0.2%), the full pose set is 264 KB (4.2%) fetched in the background, both under the 5% target; the source sheets are not published.
- [X] T047 [P] Privacy check: only `dgb-baku-mode` and `dgb-baku-dismissed` in `localStorage`, no new network requests, no seed, password or key data touched (FR-014)
- [X] T048 Confirm the CSP in `W/wwwroot/index.html` is unchanged and no inline or remote images were introduced
- [X] T049 Add a short Baku section to the relevant doc in `docs/` (for example `docs/ARCHITECTURE.md`) describing the service, catalog and evaluator
- [X] T050 Run `dotnet build` and `dotnet test` across affected projects; run the full quickstart.md — build clean; Baku, SendRiskEvaluator, KnownAddressProvider and DigiIdRiskEvaluator tests pass; the full DigiByte.Wallet.Tests run has 6 failures, all LiveExplorerBenchmarks calling live explorers (502). Quickstart run in the browser except DigiDollar send and offline.

- [ ] T051 (Optional) Run a short usability session with first-time users to measure SC-003 (self-service wallet creation and backup) and SC-004 (users describe Baku as helper and protector); record results in `specs/002-baku-mascot-guide/checklists/usability-notes.md`

---

## Phase 8: Digi-ID guidance and guardian (added after the first implementation pass)

- [X] T052 [US2] Write failing tests in `T/DigiIdRiskEvaluatorTests.cs` (known and secure is None, unknown is NewSite, HTTP is InsecureSite and beats NewSite, case and whitespace ignored, lookalike and subdomain are new, empty domain is new), then implement `L/Services/DigiIdRiskEvaluator.cs` to pass them
- [X] T053 [US2] Add `digiid.intro`, `digiid.new-site` and `digiid.insecure` to `W/Services/BakuCatalog.cs` and `BakuService.ConfirmDigiIdRiskAsync`, with tests in `T/BakuServiceTests.cs`
- [X] T054 [US2] Show the Baku intro on `W/Pages/Identity.razor` and, before a login is approved, evaluate the site against the existing login history and confirm via `Baku.ConfirmDigiIdRiskAsync`; going back cancels without signing
- [X] T055 Verify Digi-ID via quickstart.md scenario 5

---

## Dependencies & Execution Order

- **Phase 1** (assets) has no dependencies. It must finish before the pose images are used in Phases 2 to 6 (T011 can start with placeholders).
- **Phase 2** blocks all stories.
- **US1** and **US2** (both P1) are independent after Phase 2. Do US1 first as the MVP.
- **US3** needs Phase 2 only, but T035 and T036 touch the same files as US2 (`Send.razor`, `DigiDollarSend.razor`), so run them after T026 and T027.
- **US4** needs Phase 2 and shares `Settings.razor` with T034 and T038, so sequence those edits.
- **Phase 7** runs last.
- Within Phase 2, tests (T007) come before `BakuService` (T008). Within US2, tests (T022) come before the evaluator (T023), and T024 before T026 and T027.

## Parallel Opportunities

- Phase 2: T005, T011, T012 and T013 touch different files.
- US1: T015 to T018 and T020 are separate pages.
- US2: T022 and T025 and T028 can run together. T026 and T027 follow T023 and T024.
- US3: T031 to T034 and T037 are separate pages.
- Phase 7: T043 to T047 are independent checks.

## Implementation Strategy

1. **MVP**: Phases 1, 2 and US1 (onboarding guide). Validate, then demo.
2. **Next**: US2 guardian protections, the highest security value.
3. **Then**: US3 tips and US4 controls, then polish.
