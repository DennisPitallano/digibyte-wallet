---

description: "Task list for Faster Balance and History"
---

# Tasks: Faster Balance and History

**Input**: Design documents from `/specs/023-faster-balance-history/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/history-sources.md](contracts/history-sources.md), [quickstart.md](quickstart.md)

**Tests**: Required. Constitution Principle III makes tests mandatory for every change to `DigiByte.Wallet/`: written first, failing before the code change, and covering negative paths. Web-only changes are checked in the browser preview.

**Organization**: Tasks are grouped by user story (US1–US6 from spec.md) so each can be built and checked on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1–US6)

## Path Conventions

- Wallet logic: `src/DigiByte.Wallet/` (Models, Services)
- Web app: `src/DigiByte.Web/` (Pages, Services, Program.cs)
- Tests: `tests/DigiByte.Wallet.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New types and test doubles that every later phase uses

- [ ] T001 [P] Create `AddressSummary`, `AddressHistoryCursor` and `AddressHistoryPage` in src/DigiByte.Wallet/Models/AddressSummary.cs per data-model.md. `AddressSummary` fields: `Address` (key, "exactly as derived (not normalised)"), `ConfirmedTxCount` and `PendingTxCount` ("≥ 0"), `ConfirmedFunded` ("≥ 0"), `ConfirmedSpent` ("0 ≤ value ≤ `ConfirmedFunded`"), `PendingFunded` and `PendingSpent` ("≥ 0"), `ReadAtUtc`. Derived: `Confirmed`, `PendingNet` ("may be negative"), `IsUsed` ("`ConfirmedTxCount + PendingTxCount > 0`") and `DiffersFrom(other)` (true if any count or amount differs). Cursor fields: `Complete`, `Truncated`, `NewestConfirmedTxId`, `PendingTxIds`. `AddressHistoryPage(IReadOnlyList<TransactionInfo> Transactions, string? LastConfirmedTxId)`.
- [ ] T002 [P] Create the optional interface `IAddressSummarySource` (`GetAddressSummaryAsync(string)`, `GetAddressHistoryPageAsync(string, string? afterTxId)`) and `SummaryUnavailableException` in src/DigiByte.Wallet/Services/IAddressSummarySource.cs, exactly as in contracts/history-sources.md §2.
- [ ] T003 [P] Create the optional interface `IWalletHistorySource` (`GetWalletTransactionsAsync(IReadOnlyList<string>)`) in src/DigiByte.Wallet/Services/IWalletHistorySource.cs per contracts/history-sources.md §3.
- [ ] T004 [P] Create `HistorySyncOptions { bool ChangeOnly = true }` in src/DigiByte.Wallet/Services/HistorySyncOptions.cs.
- [ ] T005 [P] Add shared test doubles in tests/DigiByte.Wallet.Tests/HistorySyncTestDoubles.cs:
  - `SummaryExplorer`: implements `IBlockchainService` and `IAddressSummarySource` over a scripted chain (per-address confirmed and pending transactions). It counts requests by kind (summary, history page, tip height, balance, address-transactions), can fail chosen addresses or pages, and can add a delay.
  - A helper that builds a `WalletService` over it, reusing `TestMemoryStorage` from tests/DigiByte.Wallet.Tests/DdTestDoubles.cs.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The cache, tip height, storage and invalidation pieces every story depends on

**⚠️ CRITICAL**: No user story work starts until this phase is done

- [ ] T006 [P] Write failing tests in tests/DigiByte.Wallet.Tests/FallbackCacheTests.cs:
  - Two concurrent identical `GetBalanceAsync(list)` calls give one request to the explorer (FR-002).
  - A failed read is not cached, and the next call retries.
  - `InvalidateCache()` while a read is in flight means that read's result is not stored, and the next call goes to the network (FR-023, research R5).
- [ ] T007 [P] Write failing tests in tests/DigiByte.Wallet.Tests/EsploraPagingTests.cs for `BlockchainApiService`, using a stub `HttpMessageHandler`:
  - A history load over 10 addresses sends at most one `GET /blocks/tip/height` within 10 s (FR-001, research R6).
  - `GetTransactionAsync` reuses the same memo.
  - `GetAddressSummaryAsync` parses `chain_stats` and `mempool_stats` (`tx_count`, `funded_txo_sum`, `spent_txo_sum`), and throws when either is missing or the status is not 2xx.
  - `GetAddressHistoryPageAsync(a, null)` calls `/address/{a}/txs`; `GetAddressHistoryPageAsync(a, id)` calls `/address/{a}/txs/chain/{id}`.
- [ ] T008 Replace the cache in src/DigiByte.Wallet/Services/FallbackBlockchainService.cs (`_cache`, `CachedRead`, `InvalidateCache`) with a `CacheEntry` holding a finished value with expiry or a running `Task<object>`, plus a generation number.
  - Concurrent misses await the same task.
  - A faulted task is removed.
  - `InvalidateCache()` bumps the generation, and a completion from an older generation is not stored.
  - Keep the price entry working through the same structure. Makes T006 pass.
- [ ] T009 Add a 10-second in-flight-shared tip-height memo in src/DigiByte.Wallet/Services/BlockchainApiService.cs, used by `GetAddressTransactionsAsync` (line ~217) and `GetTransactionAsync` (line ~173) instead of calling `GetBlockHeightAsync()` directly. Makes the first T007 case pass.
- [ ] T010 Implement `IAddressSummarySource` on `BlockchainApiService` in src/DigiByte.Wallet/Services/BlockchainApiService.cs:
  - Extend the `EsploraAddressCounts` DTO with funded/spent sums for both stats.
  - `GetAddressHistoryPageAsync` maps transactions with the shared tip height and returns the last confirmed txid of the page.
  - Strict: no catch-and-empty. Makes the remaining T007 cases pass.
- [ ] T011 [P] Write failing tests in tests/DigiByte.Wallet.Tests/AddressSummaryStoreTests.cs:
  - Round-trips `addr_summary_{walletId}_{network}` and `chain_history_{walletId}_{network}`.
  - A record whose `Version` is not 1 is discarded (returns empty).
  - Wallets and networks never share records.
  - `DeleteAsync(walletId)` removes both records for every network.
- [ ] T012 Create `AddressSummaryStore` in src/DigiByte.Wallet/Services/AddressSummaryStore.cs over `ISecureStorage`:
  - `AddressSummaryRecord` holds `Version` = 1, `Entries` (address → Summary + Cursor) and `LastFullResyncUtc`.
  - `SavedChainHistory` holds `Version` = 1 and `Transactions` (txid → `TransactionInfo`, plus block height).
  - Methods: load, save, merge-by-txid and delete per wallet. Makes T011 pass.
- [ ] T013 Route `IAddressSummarySource` in src/DigiByte.Wallet/Services/FallbackBlockchainService.cs:
  - Use the first explorer that implements it and is not on cooldown, with the same `Note`/cooldown accounting as address reads.
  - Throw `SummaryUnavailableException` on regtest, when no explorer implements it, or when all fail.
  - Summary lookups are not cached here; `WalletHistorySync` keeps the snapshot.
  - Add tests for routing, cooldown and unavailable to tests/DigiByte.Wallet.Tests/FallbackCacheTests.cs.
- [ ] T014 [P] Fix the post-send invalidation key, which today removes `"cache:balance"` while Home writes `cache:balance:{walletId}`. Remove the active wallet's `cache:balance:{id}`, `cache:transactions:{id}` and `cache:timestamp:{id}` instead, in src/DigiByte.Web/Pages/Send.razor (~line 1208), src/DigiByte.Web/Pages/PsbtSign.razor (~line 537) and src/DigiByte.Web/Pages/Settings.razor (~line 1162).
- [ ] T015 Extend `WalletService.DeleteWalletAsync` in src/DigiByte.Wallet/Services/WalletService.cs to also delete `tx_history_{walletId}`, both `AddressSummaryStore` records, and the `cache:balance:`/`cache:transactions:`/`cache:timestamp:` keys for that wallet id (FR-025). Add a test in tests/DigiByte.Wallet.Tests/AddressSummaryStoreTests.cs that deleting a wallet leaves none of its keys and keeps another wallet's.

**Checkpoint**: Cache sharing, tip memo, summary source, store and cleanup are in place. `dotnet test` is green.

---

## Phase 3: User Story 1 - Refreshing a wallet with no new activity is fast (Priority: P1) 🎯 MVP

**Goal**: A warm refresh with no activity sends one summary per watched address, no history pages and at most one tip height. Pages asking for different history sizes share one fetch per address.

**Independent Test**: With a saved, current summary, refresh against `SummaryExplorer` (80 addresses). Counts: summary = 80, history pages = 0, tip height ≤ 1, and the balance and history equal the previous refresh.

### Tests for User Story 1 ⚠️

- [ ] T016 [P] [US1] Failing test in tests/DigiByte.Wallet.Tests/FallbackCacheTests.cs: `GetAddressTransactionsAsync(a, 0, 50)`, then `(a, 0, 500)`, then `(a, 0, 1000)` within the cache lifetime makes one fetch, and each call returns the right slice (FR-003).
- [ ] T017 [P] [US1] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - Warm refresh with no activity over 80 addresses gives 80 summary requests, 0 pages and ≤ 1 tip height (SC-001).
  - `GetBalanceAsync()` and `GetTransactionHistoryAsync()` called at the same time request each address's summary once (FR-004).
  - Discovery-read summaries younger than 30 s are reused by the first refresh, with no second summary request (FR-011).
  - With `HistorySyncOptions.ChangeOnly = false`, every address's history is fetched and FR-001–004 still hold (FR-010a).
  - A refresh on regtest or with `SummaryUnavailableException` falls back to today's path with the same results.

### Implementation for User Story 1

- [ ] T018 [US1] Change the history key in src/DigiByte.Wallet/Services/FallbackBlockchainService.cs `GetAddressTransactionsAsync` to `txs:{address}`. The cache holds the full list the source returned, and `skip`/`take` are applied after reading. Makes T016 pass; update any existing test in tests/DigiByte.Wallet.Tests/ParallelQueryTests.cs that asserts the old key behaviour.
- [ ] T019 [US1] Create `WalletHistorySync` in src/DigiByte.Wallet/Services/WalletHistorySync.cs per contracts/history-sources.md §4:
  - `RefreshAsync(walletId, network, addresses, ct)` is single-flight per (wallet, network).
  - It keeps an in-memory `SummarySnapshot` (entries younger than 30 s count as current) and reads missing summaries 10 at a time, strict: any failure throws (FR-006).
  - It loads the saved summaries and chain history, picks addresses where `DiffersFrom` is true or no summary or cursor exists, and fetches the newest page for those. Multi-page fetching comes in US2/US3.
  - It merges by txid into `SavedChainHistory` and persists each address's summary only after its merge succeeds (FR-010).
  - It returns `SyncResult(ConfirmedSatoshis, PendingSatoshis, Transactions, AddressesFetched)`.
  - Also implement `Offer(...)`.
- [ ] T020 [US1] Wire `WalletHistorySync` into src/DigiByte.Wallet/Services/WalletService.cs. When `HistorySyncOptions.ChangeOnly` is on and the source is an `IAddressSummarySource` (the `FallbackBlockchainService`):
  - `GetBalanceAsync()` uses `RefreshAsync`.
  - `GetTransactionHistoryAsync(skip, take)` uses `RefreshAsync(...).Transactions` in place of the per-address `GetAddressTransactionsAsync` loop (~line 1492), keeping all record-building, merging with `TransactionTracker`, `RepairUnderRecordedBatchesAsync`, the gateway `Offer` and labelling unchanged.
  - On `SummaryUnavailableException`, or when the switch is off, it uses today's path.
- [ ] T021 [US1] Seed summaries from discovery in src/DigiByte.Wallet/Services/WalletService.cs `FindUsedCountAsync` (~line 1083). When the source implements `IAddressSummarySource`, call `GetAddressSummaryAsync`, use `IsUsed` for the used check, and call `WalletHistorySync.Offer`. Otherwise keep `HasAddressHistoryAsync`, keeping its no-cooldown and no-outage semantics (FR-011, research R9).
- [ ] T022 [US1] Register `HistorySyncOptions` (singleton), `AddressSummaryStore` and `WalletHistorySync` (scoped) in src/DigiByte.Web/Program.cs and pass them into `WalletService`. At startup, load `ChangeOnly` from `ISecureStorage` key `dev:history-change-only` (missing → true).
- [ ] T023 [US1] Add a "Change-only history sync" toggle to src/DigiByte.Web/Pages/DevNode.razor that writes `dev:history-change-only` and updates `HistorySyncOptions.ChangeOnly`, taking effect on the next refresh (FR-010a).

**Checkpoint**: A warm refresh with no activity is cheap. The switch on `/dev/node` returns to today's behaviour. T016–T017 pass.

---

## Phase 4: User Story 2 - New activity is picked up without reloading everything (Priority: P1)

**Goal**: Changed addresses, and only those, get their new transactions. Pending → confirmed, dropped and newly watched addresses are handled. A send refreshes at once. A background full re-read runs once per unlock.

**Independent Test**: From a current saved summary, add one transaction to one address in `SummaryExplorer`. The next refresh pages only that address, shows the transaction, and includes it in the balance.

### Tests for User Story 2 ⚠️

- [ ] T024 [P] [US2] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - A new payment on one address pages only that address, with the right amount and direction (SC-003).
  - A pending transaction confirming (total count unchanged, confirmed +1, pending −1) is detected and its confirmations update (US2 #2).
  - A pending transaction that disappears without confirming is removed from saved history and the pending sum.
  - A newly watched address with no saved summary is fetched.
  - New confirmed transactions are fetched page by page until a page contains an already-saved confirmed txid (FR-013).
- [ ] T025 [P] [US2] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs for failures:
  - One summary lookup failing makes the balance read throw and leaves every saved summary unchanged (FR-006, SC-006).
  - A history page failing for one changed address still returns the balance, keeps that address's old summary, and retries it next cycle (FR-010).
  - `InvalidateCache()` after a send means the next `GetBalanceAsync()`/`GetTransactionHistoryAsync()` reflect the send, including when a read was in flight (FR-023).
- [ ] T026 [P] [US2] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs for the background full re-read (FR-009):
  - It starts after the first change-only result is returned, not before.
  - It waits while a `RefreshAsync` is running.
  - It uses at most two concurrent requests.
  - On success it replaces saved chain history in one write, which corrects a transaction whose block changed with no count change.
  - On failure it changes nothing returned and retries after 1 minute.
  - It runs once per unlock, and locking the wallet cancels it.

### Implementation for User Story 2

- [ ] T027 [US2] Extend `WalletHistorySync.RefreshAsync` in src/DigiByte.Wallet/Services/WalletHistorySync.cs:
  - Multi-page fetch for changed addresses that stops at `AddressHistoryCursor.NewestConfirmedTxId`.
  - Replace the address's pending set from the newest page and remove saved pending txids that are neither listed nor confirmed.
  - Update the cursor.
  - Partial page failure keeps that address's old summary and cursor while still returning the summary-based balance. Makes T024–T025 pass.
- [ ] T028 [US2] Add `FullResyncState` to the session in src/DigiByte.Wallet/Services/WalletService.cs (`UnlockedWalletSession`) and implement `WalletHistorySync.EnsureFullResync(...)` in src/DigiByte.Wallet/Services/WalletHistorySync.cs per data-model.md. The state machine is NotStarted → Running → Done, with Failed going back to NotStarted after 1 min.
  - Two requests at a time, waiting while a foreground refresh runs.
  - Full paging per address, then one write.
  - Cancelled on lock or wallet switch.
  - Call it from `GetTransactionHistoryAsync` after the change-only result is built. Makes T026 pass.
- [ ] T029 [US2] Make sure every send path in src/DigiByte.Wallet/Services/WalletService.cs (single send, batch send, consolidate, PSBT broadcast) calls `InvalidateCache()` on the `FallbackBlockchainService` and drops the `WalletHistorySync` snapshot for the wallet after broadcasting (FR-023). Add `WalletHistorySync.ForgetSnapshot(walletId)` if needed.

**Checkpoint**: New activity, confirmations, drops, failures and sends all behave correctly. US1 and US2 together are the change-only sync.

---

## Phase 5: User Story 3 - Busy addresses show their complete history (Priority: P2)

**Goal**: Addresses with more than 25 confirmed transactions show all of them, on every path, multisig included.

**Independent Test**: Give one address 60 confirmed transactions. All 60 appear, and a later refresh with one new transaction fetches one page.

### Tests for User Story 3 ⚠️

- [ ] T030 [P] [US3] Failing tests in tests/DigiByte.Wallet.Tests/EsploraPagingTests.cs:
  - `BlockchainApiService.GetAddressTransactionsAsync` on an address with 60 confirmed transactions follows `/txs/chain/{last}` and returns all 60.
  - A source that never ends stops at 2,000 transactions.
  - `skip`/`take` still apply to the full list.
- [ ] T031 [P] [US3] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - First load of a 60-transaction address saves all 60 and marks the cursor `Complete`.
  - One new transaction later fetches exactly one page (SC-004, FR-012/013).
  - Hitting the 2,000 cap sets `Truncated` and logs once.
- [ ] T032 [P] [US3] Failing test in tests/DigiByte.Wallet.Tests/ (existing multisig history tests, e.g. a new case next to `MultisigWalletService` history tests): a multisig address with 60 transactions shows all 60 (clarification 4).

### Implementation for User Story 3

- [ ] T033 [US3] Make `BlockchainApiService.GetAddressTransactionsAsync` in src/DigiByte.Wallet/Services/BlockchainApiService.cs page through `/address/{a}/txs/chain/{last}` until the end or 2,000 transactions, then apply `skip`/`take`. This serves the change-only-off path and `MultisigWalletService` (~line 1045) without changing their calls. Makes T030 and T032 pass.
- [ ] T034 [US3] In src/DigiByte.Wallet/Services/WalletHistorySync.cs, on first load of an address (no cursor), page to the end or 2,000, set `Complete`/`Truncated` and `NewestConfirmedTxId`, and log once when truncated (no addresses or amounts in the log). Makes T031 pass.

**Checkpoint**: Full history everywhere.

---

## Phase 6: User Story 4 - Incoming payments show as pending straight away (Priority: P2)

**Goal**: The balance reports a net pending amount separately. Home's main figure is confirmed only, with a "Pending ±X DGB" line.

**Independent Test**: An unconfirmed incoming payment shows on the pending line, the main figure and available-to-send exclude it, and it moves into the main figure once confirmed.

### Tests for User Story 4 ⚠️

- [ ] T035 [P] [US4] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - A pending incoming payment gives `UnconfirmedSatoshis` > 0 and `ConfirmedSatoshis` that excludes it (FR-014/015).
  - A pending outgoing payment gives a negative `UnconfirmedSatoshis`.
  - After confirmation, pending is 0 and confirmed includes it.
  - On the full-read path, `UnconfirmedSatoshis` = 0.

### Implementation for User Story 4

- [ ] T036 [US4] Set `WalletBalance.UnconfirmedSatoshis` from `SyncResult.PendingSatoshis` (sum of `PendingNet`, "may be negative") in `WalletService.GetBalanceAsync()` in src/DigiByte.Wallet/Services/WalletService.cs. Makes T035 pass.
- [ ] T037 [US4] Audit every `TotalDgb` use in src/DigiByte.Web (Home, Send, PsbtSign, Receive, components). Where it means the balance shown or spendable, switch to `ConfirmedDgb`; keep `TotalDgb` meaning confirmed + pending in src/DigiByte.Wallet/Models/WalletInfo.cs.
- [ ] T038 [US4] In src/DigiByte.Web/Pages/Home.razor, show `ConfirmedDgb` as the main figure and base the fiat value on it. When `UnconfirmedDgb` ≠ 0, add a separate line "Pending +X DGB" / "Pending −X DGB" that respects `hideBalance`. The balance animation keys on the confirmed figure, so a dropped pending payment doesn't move it (FR-016). Check in the browser preview.

**Checkpoint**: Pending is visible and never spendable.

---

## Phase 7: User Story 5 - Wallet pages show the last known figures immediately (Priority: P2)

**Goal**: Transactions, Receive and Send show the saved snapshot at once and refresh in the background, with "Last updated" when the refresh fails.

**Independent Test**: After one Home load, open each page with a slow network. Saved figures appear within 1 s and are replaced when the fresh answer arrives. Offline, they stay with "Last updated …".

### Tests for User Story 5 ⚠️

- [ ] T039 [P] [US5] Failing tests in tests/DigiByte.Wallet.Tests/FallbackCacheTests.cs for the opt-in stale read:
  - An expired balance or history entry is returned at once while one background refresh runs.
  - A stale read never applies to `GetUtxosAsync`/`GetVerifiedUtxosAsync`.
  - After `InvalidateCache()`, no value from the previous generation is returned stale (FR-019, FR-022/023).

### Implementation for User Story 5

- [ ] T040 [US5] Add the opt-in stale read for balance and history keys in src/DigiByte.Wallet/Services/FallbackBlockchainService.cs: serve the expired value and refresh in the background, respecting the generation. Makes T039 pass.
- [ ] T041 [US5] Create `WalletSnapshotCache` in src/DigiByte.Web/Services/WalletSnapshotCache.cs. It reads, writes and invalidates `cache:balance:{walletId}`, `cache:transactions:{walletId}`, `cache:price` and `cache:timestamp:{walletId}` exactly as Home does today (src/DigiByte.Web/Pages/Home.razor ~lines 616, 830–946), and exposes the last-updated time. Register it in src/DigiByte.Web/Program.cs, then move Home's snapshot code to it, with no change in Home's behaviour. Use it in T014's invalidation spots too.
- [ ] T042 [P] [US5] In src/DigiByte.Web/Pages/Transactions.razor, show the snapshot at once (balance and history for the active wallet only), refresh in the background, and on failure keep the snapshot with "Last updated …" (FR-017/018/024).
- [ ] T043 [P] [US5] In src/DigiByte.Web/Pages/Receive.razor, show the snapshot history at once in place of waiting on `GetTransactionHistoryAsync(0, 1000)` (~line 413), refreshing in the background; same failure rule.
- [ ] T044 [P] [US5] In src/DigiByte.Web/Pages/Send.razor, show the snapshot balance at once as the displayed available amount, refreshing in the background. The coins for the send are still read fresh at confirm time with no change to the signing path (FR-022), and the snapshot is invalidated after a send.
- [ ] T045 [US5] Check src/DigiByte.Web/Pages/Transactions.razor, Receive.razor and Send.razor in the browser preview with DevTools throttling: each page shows saved data within 1 s; offline shows "Last updated …"; switching wallets never shows another wallet's figures (SC-005, US5 #3).

**Checkpoint**: Every wallet page feels instant.

---

## Phase 8: User Story 6 - Gateway users get history without heavy processing (Priority: P3)

**Goal**: With the gateway answering history, the wallet's history is built in one pass over the saved record.

**Independent Test**: With 80 addresses and 200 transactions, each transaction is parsed once, and the result equals the per-address result.

### Tests for User Story 6 ⚠️

- [ ] T046 [P] [US6] Failing tests in tests/DigiByte.Wallet.Tests/GatewayWalletHistoryTests.cs, using the gateway doubles from tests/DigiByte.Wallet.Tests/P2pGatewayBlockchainServiceTests.cs:
  - `GetWalletTransactionsAsync(80 addresses)` over 200 transactions equals the union of per-address `GetAddressTransactionsAsync` results (txids, inputs, outputs, confirmations) (FR-021).
  - Each raw transaction is parsed once (SC-007).
  - `CannotAnswer` still throws as the per-address method does.
  - `FallbackBlockchainService.TryGetWalletHistorySource()` returns the gateway only while it would answer history.

### Implementation for User Story 6

- [ ] T047 [US6] Implement `IWalletHistorySource.GetWalletTransactionsAsync` on src/DigiByte.Wallet/Services/P2pGatewayBlockchainService.cs: work out each address's script once, parse each `SyncTransaction.Raw` once, keep the ones that pay or spend any script, and order them as `GetAddressTransactionsAsync` does.
- [ ] T048 [US6] Add `TryGetWalletHistorySource()` to src/DigiByte.Wallet/Services/FallbackBlockchainService.cs (gateway active and in First position, or Backup with every explorer failed) and use it in `WalletService.GetTransactionHistoryAsync` in src/DigiByte.Wallet/Services/WalletService.cs before the per-address loop. Verification labelling stays unchanged. Makes T046 pass.

**Checkpoint**: All six stories work.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T049 Create `RequestCountingHandler` in src/DigiByte.Web/Services/RequestCountingHandler.cs: a `DelegatingHandler` that counts only while a session is open. It groups requests as `/address/{a}` = Summary, `/address/{a}/txs…` = HistoryPage, `/blocks/tip/height` = TipHeight, and anything else = Other. Counts are in memory only, never logged, saved or sent (FR-010b). Register it on the explorer `HttpClient` in src/DigiByte.Web/Program.cs.
- [ ] T050 Add a "Wallet refresh benchmark" to src/DigiByte.Web/Pages/DevNode.razor (needs an unlocked wallet). It runs balance plus history four times: on/cold, on/warm, off/cold, off/warm, clearing the Fallback cache, snapshot and saved summaries for the cold runs. For each run it shows the time and the Summary, HistoryPage, TipHeight and Other counts, then puts the switch back to its previous value.
- [ ] T051 [P] Update docs/ARCHITECTURE.md (read path: address summaries, change-only sync, saved chain history, background full re-read, `/dev/node` switch) and docs/PROCESS_FLOWS.md if it describes balance or history refresh.
- [ ] T052 Run `dotnet build` and `dotnet test` for the whole solution; fix anything this feature broke in existing tests (e.g. tests/DigiByte.Wallet.Tests/TransactionHistoryAddressTests.cs, GatewayWiringTests.cs, ParallelQueryTests.cs).
- [ ] T053 Run quickstart.md sections 2 and 3 against the live explorer in the browser preview. Record the `/dev/node` on/off warm times and counts in the PR description as proof of SC-001 and SC-002.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none. T001–T005 can all run in parallel.
- **Foundational (Phase 2)**: needs Phase 1. Blocks every story.
- **US1 (Phase 3)**: needs Phase 2. This is the MVP.
- **US2 (Phase 4)**: needs US1 (it extends `WalletHistorySync.RefreshAsync`).
- **US3 (Phase 5)**: T033 needs only Phase 2; T034 needs US1 (T019).
- **US4 (Phase 6)**: needs US1 (T019/T020).
- **US5 (Phase 7)**: needs Phase 2 only. It is independent of the sync and can run alongside US1–US4.
- **US6 (Phase 8)**: needs Phase 2 only. It is independent of the sync.
- **Polish (Phase 9)**: T049–T050 need US1 (switch and sync); T052–T053 come last.

### Within Each Story

Tests first, and they must fail. Then the model/store, then the service, then the WalletService wiring, then the Web page. Commit after each task or logical group.

### Parallel Opportunities

- Phase 1: T001–T005.
- Phase 2: T006, T007 and T011 (tests in different files), and T014 (Web pages) next to any wallet task.
- After Phase 2: US5 (T039–T045) and US6 (T046–T048) can run alongside US1, because they touch different files. The exception is that T040 and T048 also touch `FallbackBlockchainService.cs`, so run them after T018.
- Within US5: T042, T043 and T044 (three different pages).

## Parallel Example: after Phase 2

```text
Task: "T017 [US1] HistorySyncTests no-activity cases in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs"
Task: "T046 [US6] GatewayWalletHistoryTests in tests/DigiByte.Wallet.Tests/GatewayWalletHistoryTests.cs"
Task: "T042 [US5] Transactions.razor saved snapshot"
Task: "T043 [US5] Receive.razor saved snapshot"
```

## Implementation Strategy

### MVP (User Story 1)

Phases 1–3. The no-activity refresh drops from about 3N to N + 1 requests, the switch gives a quick way back, and the existing guarantees hold. Stop, run quickstart §1 (US1 rows), and ship behind the default-on switch. Ship it only together with US2, though: US1 on its own catches new activity only on an address's newest page.

**Recommended first release**: US1 + US2 (both P1), then US3 + US4, then US5, then US6 + the benchmark.

### Incremental Delivery

1. Phase 2 alone already helps: the tip-height memo removes about N height requests from every history load (`BlockchainApiService`; also benefits Pay.Api), and in-flight sharing removes duplicate reads.
2. US1 + US2 → change-only sync.
3. US3 → complete history (also multisig).
4. US4 → pending line.
5. US5 → instant pages.
6. US6 + Polish → gateway one-pass, benchmark proof, docs.

## Notes

- Never log addresses, amounts or txids from the new code (constitution: secret hygiene and privacy).
- Keep the `IBlockchainService` signature unchanged; the new abilities are optional interfaces.
- Conventional Commits: `feat(wallet): ...`, `fix(web): ...`, `test(wallet): ...`.
