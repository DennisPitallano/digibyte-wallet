# Tasks: Pull-Down Refresh on Multi-Sig Wallet Dashboard

**Input**: Design documents from `/specs/021-multisig-pull-refresh/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: No automated test tasks were requested, and the repo has no component-test framework. Verification is manual per [quickstart.md](quickstart.md).

**Format**: `- [ ] T### [P?] [Story] Description with file path`. Code changes are in `src/DigiByte.Web/Pages/MultisigDetail.razor` unless noted, so most tasks are sequential (no `[P]`).

## Phase 1: Setup

- [ ] T001 Note how pull-to-refresh looks and feels on `/transactions` in touch-emulated DevTools (spinner, threshold, release), as the reference for FR-008 and SC-004 (no file changes)

## Phase 2: Foundational

- [X] T002 In `src/DigiByte.Web/Pages/MultisigDetail.razor`, inject `NotificationService` (`@inject NotificationService Notifications`) and add a page-level `private bool isRefreshing;` flag (research R4, data-model `isRefreshing`)
- [X] T003 Check what `GetActivityAsync` in `src/DigiByte.Wallet/Services/MultisigWalletService.cs` (around line 914) returns when the network is unavailable: a thrown exception, or a result with `IsPartial = true` and no entries. Record the finding in `specs/021-multisig-pull-refresh/research.md` under R2 (FR-007)
- [X] T003a (not needed: offline, the service returns a partial page instead of throwing, so no change) Only if T003 shows the service gives no failure signal offline: in `src/DigiByte.Wallet/Services/MultisigWalletService.cs`, make the activity result say when it failed, and add a unit test for that case in `tests/DigiByte.Wallet.Tests/MultisigActivityBuilderTests.cs`, written before the change (constitution Principle III)
- [X] T004 In `src/DigiByte.Web/Pages/MultisigDetail.razor`, refactor `LoadBalance()` and `LoadActivity()` so each takes an optional `keepPreviousOnFailure` argument (default false) and returns `Task<bool>` (true on success; false when the call threw or, for activity, failed or came back partial with no entries, per T003). With the default, behaviour for the first page load and the refresh button is exactly as today: on failure `LoadBalance` sets `balanceSatoshis = null` and `LoadActivity` sets `activityPartial = true`. With `keepPreviousOnFailure = true`, a failure leaves `balanceSatoshis`, `recentActivity` and `activityPartial` untouched, so the old balance is never replaced by "unknown", not even briefly (research R2; FR-007, FR-009)
- [X] T005 In `src/DigiByte.Web/Pages/MultisigDetail.razor`, make the first-load path (`OnParametersSetAsync`, which starts `LoadBalance`, `LoadPrice` and `LoadActivity` without waiting for them) set `isRefreshing = true` until the balance and activity loads finish, so a pull or button tap right after the page appears does not start an overlapping load (spec Edge Cases). Clear the flag in a `finally` block, and reset it when `WalletId` changes so a stuck flag can never block later pulls

**Checkpoint**: page still builds and behaves as before.

## Phase 3: User Story 1 - Pull down to refresh the dashboard (Priority: P1) 🎯 MVP

**Goal**: Pulling down at the top of the single multi-sig wallet page reloads balance and recent activity.

**Independent Test**: Quickstart steps 1 and 2 (happy path, short pull).

- [X] T006 [US1] In `src/DigiByte.Web/Pages/MultisigDetail.razor`, wrap the loaded-wallet page body (the `<div class="flex flex-col gap-3">` block in the final `else` branch) in `<PullToRefresh OnRefresh="PullRefresh">…</PullToRefresh>`, keeping `<ReceiveSheet>` and `<RenameDialog>` as siblings after `</PullToRefresh>` (research R1, R3; FR-001, FR-008)
- [X] T007 [US1] In `src/DigiByte.Web/Pages/MultisigDetail.razor`, add `private async Task PullRefresh()` that returns immediately if `isRefreshing`, otherwise sets `isRefreshing = true`, awaits `LoadBalance(keepPreviousOnFailure: true)` and `LoadActivity(keepPreviousOnFailure: true)` (same data as the button: balance and activity only; FR-002), and clears the flag in a `finally` block
- [ ] T008 [US1] Run quickstart steps 1 and 2 and confirm the balance and activity update on a pull, and a short pull does nothing

**Checkpoint**: MVP complete: pull-to-refresh works on the happy path.

## Phase 4: User Story 2 - No interference with normal scrolling (Priority: P2)

**Goal**: Mid-page scrolling and overlays never trigger a refresh.

**Independent Test**: Quickstart steps 3 and 4.

- [ ] T009 [US2] Verify quickstart step 3 (scroll down, then swipe down: no refresh) and step 4 (swipe inside the Receive sheet, the Rename dialog, the expanded co-signers panel and the "Remove this multisig" confirmation: no refresh) on `/multisig/<id>`
- [ ] T010 [US2] Only if T009 step 4 fails: in `src/DigiByte.Web/Components/Shared/PullToRefresh.razor`, make `OnTouchStart` skip arming when the touch starts inside an open overlay, dialog or confirmation, then re-run the `/transactions` check from T001 to confirm no regression (research R3; FR-004)

**Checkpoint**: gesture only arms at the top of the page with nothing open over it.

## Phase 5: User Story 3 - Failure feedback and repeat pulls (Priority: P3)

**Goal**: A failed pull keeps the old data and says so; repeat pulls and button taps don't start a second refresh.

**Independent Test**: Quickstart steps 5, 6 and 7.

- [X] T011 [US3] In `src/DigiByte.Web/Pages/MultisigDetail.razor`, update `PullRefresh()` so that if either load returns false it calls `Notifications.Error("Couldn't refresh")` once per pull. The previous balance and activity are already kept by `keepPreviousOnFailure` (research R2; FR-007)
- [X] T012 [US3] In `src/DigiByte.Web/Pages/MultisigDetail.razor`, make the existing `Refresh()` (the balance card button handler) return immediately while `isRefreshing` is true, and set and clear `isRefreshing` around its work so a pull and a button tap never overlap. Its behaviour otherwise stays unchanged, with no toast (research R4; FR-006, FR-009)
- [ ] T013 [US3] Run quickstart steps 5, 6 and 7 (offline pull, repeat and early pulls, button unchanged online and offline)

**Checkpoint**: all three stories work independently.

## Phase 6: Polish & Cross-Cutting

- [ ] T014 [P] Run quickstart step 8: confirm `/multisig` (the wallet list) has no pull-to-refresh, and re-check `/transactions` pull-to-refresh is unchanged
- [X] T015 Run `dotnet build` and `dotnet test` from the repo root and fix any failures
- [ ] T016 Commit locally with a Conventional Commit message (`feat(multisig): pull-down refresh on the wallet dashboard`); push only when asked

## Dependencies & Execution Order

- Phase 1 has no dependencies. Phase 2 follows it and blocks everything after. Inside Phase 2, the order is T003, then T003a (only if needed), then T004, and T004 and T005 need T002.
- US1 (Phase 3) needs Phase 2. US2 (Phase 4) needs T006 (the wrapper must exist to test). US3 (Phase 5) needs T004 and T007.
- Polish (Phase 6) runs last.
- Almost every task edits `MultisigDetail.razor`, so they run in order. Only T014 is marked `[P]`.

## Implementation Strategy

- **MVP**: Phases 1 to 3 (US1) deliver a working pull-to-refresh.
- **Then**: US2 (verification, with a conditional fix), then US3 (failure toast and the single-refresh guard).
- **Total**: 17 tasks (Setup 1, Foundational 5, US1 3, US2 2, US3 3, Polish 3). T003a and T010 are conditional.
