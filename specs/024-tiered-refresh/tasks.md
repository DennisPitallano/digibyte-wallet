---

description: "Task list for Fewer Requests per Refresh and per Send"
---

# Tasks: Fewer Requests per Refresh and per Send

**Input**: Design documents from `/specs/024-tiered-refresh/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/tiered-refresh.md](contracts/tiered-refresh.md), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution Principle III makes tests mandatory for every change to `DigiByte.Wallet/`: written first, failing before the code change, and covering negative paths. Web-only changes are checked in the browser preview.

**Organization**: Tasks are grouped by user story (US1–US4 from spec.md).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1–US4)

## Path Conventions

- Wallet logic: `src/DigiByte.Wallet/Services/`
- Web app: `src/DigiByte.Web/` (Pages, Program.cs)
- Tests: `tests/DigiByte.Wallet.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The switch, the value types and the test doubles every later phase uses

- [ ] T001 [P] Add `Tiered` (default `true`) and `TieredStorageKey = "dev:tiered-refresh"` to `HistorySyncOptions` in src/DigiByte.Wallet/Services/HistorySyncOptions.cs, with a doc comment that tiering applies only while `ChangeOnly` is also on (clarification 4).
- [ ] T002 [P] Create the value types in src/DigiByte.Wallet/Services/HotAddressSelector.cs and src/DigiByte.Wallet/Services/SendCoinPlanner.cs (types only, methods throw `NotImplementedException`): `HotAddressHints` (`OnShow`, `HandedOut` "newest first, at most 3", `NextChange`, `PendingSendInputs`), `HotAddressInputs` (`Watched`, `Summaries`, hints, `RecentActivity`), `HotSet` (`Hot`, `Cold`, `Tiered`), and `SendCoinPlan` (`Funded`, `Rest`, `Planned`), per data-model.md.
- [ ] T003 [P] Extend the test doubles in tests/DigiByte.Wallet.Tests/HistorySyncTestDoubles.cs:
  - `SummaryExplorer` gets scripted spendable coins per address (`Coins[address]`, with the address's script) and counts `GetUtxosAsync` calls per address (`CoinReads`, `CoinReadAddresses`).
  - `HistorySyncEnv` exposes `Options.Tiered`, and a helper that funds receive addresses with real coins so sends can be built and signed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The two pure rules every story depends on

**⚠️ CRITICAL**: No user story work starts until this phase is done

- [ ] T004 [P] Write failing tests in tests/DigiByte.Wallet.Tests/HotAddressSelectorTests.cs:
  - Hot includes: an address with `Confirmed > 0`; one with only pending activity; `OnShow`; each of the `HandedOut` (at most 3 kept); `NextChange`; `PendingSendInputs`; `RecentActivity`.
  - An unused, unfunded address not in any hint is cold.
  - An address that was cold becomes hot as soon as its summary shows activity (FR-003).
  - Fewer than 10 watched addresses → `Tiered = false` and every address is hot (FR-016).
  - `Hot ∪ Cold = Watched` and they never overlap.
- [ ] T005 Implement `HotAddressSelector.Select` in src/DigiByte.Wallet/Services/HotAddressSelector.cs per contracts §1 (`MinAddressesToTier = 10`, `HandedOutKept = 3`, `RecentActivityWindow = 7 days`). Makes T004 pass.
- [ ] T006 [P] Write failing tests in tests/DigiByte.Wallet.Tests/SendCoinPlannerTests.cs:
  - With current summaries, `Funded` = addresses with `Confirmed > 0` or any pending count/amount; `Rest` = the others; `Planned = true`.
  - `summaries == null` → `Planned = false`, `Funded` = every address, `Rest` empty (FR-011).
  - `NeedsRest` is true when found < amount + 0.01 DGB and `Rest` is not empty; false otherwise.
- [ ] T007 Implement `SendCoinPlanner.Plan` and `NeedsRest` in src/DigiByte.Wallet/Services/SendCoinPlanner.cs per contracts §3 (`FeeAllowanceSatoshis = 1_000_000`). Makes T006 pass.

**Checkpoint**: Both rules are pure and tested. `dotnet test` is green.

---

## Phase 3: User Story 1 - A wallet left open costs little (Priority: P1) 🎯 MVP

**Goal**: A quiet automatic refresh reads only the hot addresses (plus the spread share of cold ones).

**Independent Test**: 60 watched addresses, 2 funded; after a full check, advance the clock 31 s and refresh: summaries read ≤ 11, no history pages, balance unchanged.

### Tests for User Story 1 ⚠️

- [ ] T008 [P] [US1] Failing tests in tests/DigiByte.Wallet.Tests/TieredRefreshTests.cs (using `HistorySyncEnv` and `FakeTimeProvider`):
  - Quiet refresh 31 s after a full check reads only the hot addresses (SC-001), and no history pages.
  - Cold addresses are read in a spread: over 5 simulated minutes of 15 s refreshes, each refresh reads about a twentieth of them, none waits 5 minutes or more, and no single refresh reads more than hot + a fifth of cold (FR-004, FR-009).
  - At the 60 s pace the 5-minute bound still holds.
  - 10 simulated minutes at 15 s and at 60 s: total requests ≥ 60% fewer than with `Tiered = false` (SC-002).
  - A failed hot summary read fails the refresh and leaves saved summaries unchanged (FR-007).
  - The balance equals the sum of the summaries used, including reused cold ones (FR-007).
  - `Tiered = false`, or `ChangeOnly = false`: every address read every refresh, as in 0.7.0 (FR-013).
  - A wallet with fewer than 10 watched addresses is never tiered (FR-016).

### Implementation for User Story 1

- [ ] T009 [US1] In src/DigiByte.Wallet/Services/WalletHistorySync.cs, add `HotAddressHints? hints` to `RefreshAsync` and, when tiering applies (`Tiered && ChangeOnly && hints != null`):
  - Build `HotAddressInputs` (snapshot summaries, hints, recent activity from the in-memory `SavedChainHistory`) and call `HotAddressSelector.Select`.
  - Hot: reuse snapshot entries younger than 30 s. Cold: reuse younger than `ColdCheckInterval` (5 min), except the spread share `ceil(coldCount × min(elapsed, 5 min) / 5 min)` oldest, which are read.
  - Keep a per-wallet `LastRefreshAt` (`RefreshCursor`).
  - Otherwise keep the 0.7.0 rule. Makes T008 pass.
- [ ] T010 [US1] In src/DigiByte.Wallet/Services/WalletService.cs, build `HotAddressHints` for HD wallets (receive SegWit at `NextReceivingIndex`; up to 3 handed-out receive addresses below it; change SegWit at `NextChangeIndex`; wallet addresses spent by pending transactions in the tracked history) and pass them from `GetBalanceAsync()` and `GetTransactionHistoryAsync()`.
- [ ] T011 [P] [US1] In src/DigiByte.Web/Program.cs, load `dev:tiered-refresh` at startup into `HistorySyncOptions.Tiered` (missing → true). In src/DigiByte.Web/Pages/DevNode.razor, add a "Tiered refresh" switch row (same `.dn-switch` style) that writes the key, with a note that it needs change-only sync.

**Checkpoint**: Quiet refreshes are cheap; the switch returns to 0.7.0 behaviour.

---

## Phase 4: User Story 2 - Payments still show up (Priority: P1)

**Goal**: Hot addresses show payments at once; any address within 5 minutes; user refreshes, unlock and sends check everything.

**Independent Test**: Pay the address on show → next refresh shows it. Pay a cold address → shown within 5 simulated minutes, and at once after `RequestFullCheck()`.

### Tests for User Story 2 ⚠️

- [ ] T012 [P] [US2] Failing tests in tests/DigiByte.Wallet.Tests/TieredRefreshTests.cs:
  - A payment to the address on show, or to a handed-out address, shows on the next refresh.
  - A payment to a cold address shows within 5 simulated minutes, and on the next refresh after `RequestFullCheck()` (FR-006).
  - The first refresh after unlock and the first after a send read every address (FR-006, FR-012).
  - After a cold address is paid, it is hot on the following refreshes (FR-003).
  - `GetFullyCheckedAt` returns the oldest stamp, resets after a full check, is never older than 5 minutes during automatic refreshes, and is null before the first refresh (FR-008).

### Implementation for User Story 2

- [ ] T013 [US2] Add `WalletHistorySync.GetFullyCheckedAt(walletId, network, addresses)` and `WalletService.RequestFullCheck()` / `GetFullyCheckedAt()` in src/DigiByte.Wallet/Services/WalletHistorySync.cs and src/DigiByte.Wallet/Services/WalletService.cs (`RequestFullCheck` drops the active wallet's snapshot via `ForgetSnapshot`). Makes T012 pass.
- [ ] T014 [P] [US2] Call `WalletService.RequestFullCheck()` at the start of `ManualRefreshAsync` in src/DigiByte.Web/Pages/Home.razor (button and pull gesture) and of the pull-to-refresh handler in src/DigiByte.Web/Pages/Transactions.razor (not of its first load). Page opens stay automatic (clarification 5).
- [ ] T015 [US2] In the wallet info panel of src/DigiByte.Web/Pages/Home.razor (`showWalletInfo`), show "Fully checked N min ago" (or "just now") from `GetFullyCheckedAt()`; hidden when unknown. Never on the main balance card (FR-008). Check in the browser preview.

**Checkpoint**: Tiering never hides a payment for more than 5 minutes, and a manual refresh always shows everything.

---

## Phase 5: User Story 3 - Sending reads only the coins it needs (Priority: P2)

**Goal**: A send reads coins only for funded addresses, falling back to all before "Insufficient funds".

**Independent Test**: 60 addresses, 2 funded: confirming a send makes ≤ 4 coin reads, and the signed transaction is the same as with tiering off.

### Tests for User Story 3 ⚠️

- [ ] T016 [P] [US3] Failing tests in tests/DigiByte.Wallet.Tests/TieredRefreshTests.cs:
  - `SendAsync` with 2 funded of 60 and current summaries reads coins only for those 2 (SC-004); its inputs and outputs equal the tiering-off result.
  - A summary that under-reports (an address shows 0 but holds coins) and a send needing them: the rest are read and the send succeeds; "Insufficient funds" only when every address was read and the total is short (FR-010, SC-005).
  - No current summaries (fresh session, regtest-style `Unavailable`, either switch off): every address's coins are read (FR-011).
  - `SendBatchAsync` and `ConsolidateUtxosAsync` follow the same plan.
  - After the send, the next refresh reads every address (FR-012).

### Implementation for User Story 3

- [ ] T017 [US3] Add `WalletHistorySync.TryGetCurrentSummaries(walletId, network, addresses)` in src/DigiByte.Wallet/Services/WalletHistorySync.cs (null unless every address has a snapshot entry current under the tiered reuse rule).
- [ ] T018 [US3] In src/DigiByte.Wallet/Services/WalletService.cs, replace the all-addresses coin loops of `SendAsync` (~line 1286), `SendBatchAsync` (~1457) and `ConsolidateUtxosAsync` (~1949) with one helper: plan with `SendCoinPlanner`, read `Funded` (fresh, 10 at a time), read `Rest` when `NeedsRest` (consolidate: when the coins found total less than the summaries' confirmed sum), then continue unchanged. Add `PlanSendCoinReads()`. Makes T016 pass.

**Checkpoint**: Sends are quicker and never fail for want of reading an address.

---

## Phase 6: User Story 4 - Developers can see the difference (Priority: P3)

**Goal**: The benchmark shows quiet-refresh requests with tiering on and off, and a send's coin reads.

**Independent Test**: Run the benchmark with an unlocked wallet; rows "Tiered on, quiet", "Tiered off, quiet" and "Coins read by a send" appear with counts.

- [ ] T019 [P] [US4] Failing test then implementation: `WalletHistorySync.AgeSnapshot(walletId, by)` moves every stamp of the wallet back by `by`; after a full check and `AgeSnapshot(31 s)`, a refresh reads only the hot set (tests/DigiByte.Wallet.Tests/TieredRefreshTests.cs, src/DigiByte.Wallet/Services/WalletHistorySync.cs).
- [ ] T020 [US4] In src/DigiByte.Web/Pages/DevNode.razor, add benchmark runs "Tiered on, quiet" and "Tiered off, quiet" (each: full check, `AgeSnapshot(31 s)`, one balance-plus-history refresh, counted) and a "Coins read by a send" tile from `PlanSendCoinReads()`. Restore both switches afterwards. Check in the browser preview.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T021 [P] Update docs/ARCHITECTURE.md (read path: hot set, cold cycle, full checks, send coin planning) and docs/PROCESS_FLOWS.md (refresh and send flows).
- [ ] T022 [P] Add the user-facing changes to `[Unreleased]` in CHANGELOG.md.
- [ ] T023 Run `dotnet build` and `dotnet test`; fix anything this feature broke in existing tests (e.g. tests/DigiByte.Wallet.Tests/HistorySyncTests.cs request-count assertions now under tiering).
- [ ] T024 Run quickstart.md sections 2 and 3 against mainnet in the browser preview; record the quiet-refresh counts and send coin reads in the PR as proof of SC-001 and SC-004.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none; T001–T003 in parallel.
- **Foundational (Phase 2)**: needs Phase 1. T004/T005 and T006/T007 are independent pairs.
- **US1 (Phase 3)**: needs T005. MVP.
- **US2 (Phase 4)**: needs US1 (T009) for the tiered rule it checks around.
- **US3 (Phase 5)**: needs T007 and T009 (current summaries come from the tiered rule).
- **US4 (Phase 6)**: needs US1 and US3 (T018 for `PlanSendCoinReads`).
- **Polish (Phase 7)**: last.

### Within Each Story

Tests first, and they must fail. Then the wallet service, then the Web page. Commit after each task or logical group.

### Parallel Opportunities

- Phase 1: T001, T002, T003.
- Phase 2: T004 and T006 (tests in different files).
- T011 and T014 (Web) next to the wallet tasks of their phase.
- Polish: T021, T022.

## Parallel Example: Phase 2

```text
Task: "T004 HotAddressSelectorTests in tests/DigiByte.Wallet.Tests/HotAddressSelectorTests.cs"
Task: "T006 SendCoinPlannerTests in tests/DigiByte.Wallet.Tests/SendCoinPlannerTests.cs"
```

## Implementation Strategy

### MVP (User Story 1)

Phases 1–3 cut quiet refreshes from ~60 to ~10 requests behind a default-on switch. Ship it only together with US2, though: US2's full checks on manual refresh are what keep a payment to an old address from waiting 5 minutes when the user asks.

**Recommended release**: US1 + US2 (both P1), then US3, then US4 and the docs.

### Incremental Delivery

1. Phase 2: the pure rules, no behaviour change.
2. US1 + US2 → tiered refresh with full checks where the user expects them.
3. US3 → cheaper sends.
4. US4 + Polish → benchmark proof, docs, changelog.

## Notes

- Never log addresses, amounts or txids from the new code.
- `IBlockchainService` stays unchanged; the hints are an optional parameter.
- Conventional Commits: `feat(wallet): ...`, `feat(web): ...`, `test(wallet): ...`.
