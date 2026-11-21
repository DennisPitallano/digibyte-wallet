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

- [X] T000a [P] Failing tests in tests/DigiByte.Wallet.Tests/AddressSummaryTests.cs: `Confirmed` and `PendingNet` (including negative), `IsUsed` (0/0 false, pending-only true), and `DiffersFrom` true for each single changed field and false for equal values.
- [X] T001 [P] Create `AddressSummary`, `AddressHistoryCursor` and `AddressHistoryPage` in src/DigiByte.Wallet/Models/AddressSummary.cs per data-model.md. `AddressSummary` fields: `Address` (key, "exactly as derived (not normalised)"), `ConfirmedTxCount` and `PendingTxCount` ("≥ 0"), `ConfirmedFunded` ("≥ 0"), `ConfirmedSpent` ("0 ≤ value ≤ `ConfirmedFunded`"), `PendingFunded` and `PendingSpent` ("≥ 0"), `ReadAtUtc`. Derived: `Confirmed`, `PendingNet` ("may be negative"), `IsUsed` ("`ConfirmedTxCount + PendingTxCount > 0`") and `DiffersFrom(other)` (true if any count or amount differs). Cursor fields: `Complete`, `Truncated`, `NewestConfirmedTxId`, `PendingTxIds`. `AddressHistoryPage(IReadOnlyList<TransactionInfo> Transactions, string? LastConfirmedTxId)`. Makes T000a pass.
- [X] T002 [P] Create the optional interface `IAddressSummarySource` (`GetAddressSummaryAsync(string address, bool recordFailure = true)`, `GetAddressHistoryPageAsync(string, string? afterTxId)`) and `SummaryUnavailableException` in src/DigiByte.Wallet/Services/IAddressSummarySource.cs, exactly as in contracts/history-sources.md §2.
- [X] T003 [P] Create the optional interface `IWalletHistorySource` (`GetWalletTransactionsAsync(IReadOnlyList<string>)`) in src/DigiByte.Wallet/Services/IWalletHistorySource.cs per contracts/history-sources.md §3.
- [X] T004 [P] Create `HistorySyncOptions { bool ChangeOnly = true }` in src/DigiByte.Wallet/Services/HistorySyncOptions.cs.
- [X] T005 [P] Add shared test doubles in tests/DigiByte.Wallet.Tests/HistorySyncTestDoubles.cs:
  - `SummaryExplorer`: implements `IBlockchainService` and `IAddressSummarySource` over a scripted chain (per-address confirmed and pending transactions). It counts requests by kind (summary, history page, tip height, balance, address-transactions), can fail chosen addresses or pages, and can add a delay.
  - A helper that builds a `WalletService` over it, reusing `TestMemoryStorage` from tests/DigiByte.Wallet.Tests/DdTestDoubles.cs.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The cache, tip height, storage and invalidation pieces every story depends on

**⚠️ CRITICAL**: No user story work starts until this phase is done

- [X] T006 [P] Write failing tests in tests/DigiByte.Wallet.Tests/FallbackCacheTests.cs:
  - Two concurrent identical `GetBalanceAsync(list)` calls give one request to the explorer (FR-002).
  - A failed read is not cached, and the next call retries.
  - `InvalidateCache()` while a read is in flight means that read's result is not stored, and the next call goes to the network (FR-023, research R5).
- [X] T007 [P] Write failing tests in tests/DigiByte.Wallet.Tests/EsploraPagingTests.cs for `BlockchainApiService`, using a stub `HttpMessageHandler`:
  - A history load over 10 addresses sends at most one `GET /blocks/tip/height` within 10 s (FR-001, research R6).
  - A history load plus a `GetBlockHeightAsync()` call within 10 s, including when they overlap, sends one `GET /blocks/tip/height` in total.
  - `GetTransactionAsync` reuses the same memo.
  - `GetAddressSummaryAsync` parses `chain_stats` and `mempool_stats` (`tx_count`, `funded_txo_sum`, `spent_txo_sum`), and throws when either is missing or the status is not 2xx.
  - `GetAddressHistoryPageAsync(a, null)` calls `/address/{a}/txs`; `GetAddressHistoryPageAsync(a, id)` calls `/address/{a}/txs/chain/{id}`.
- [X] T008 Replace the cache in src/DigiByte.Wallet/Services/FallbackBlockchainService.cs (`_cache`, `CachedRead`, `InvalidateCache`) with a `CacheEntry` holding a finished value with expiry or a running `Task<object>`, plus a generation number.
  - Concurrent misses await the same task.
  - A faulted task is removed.
  - `InvalidateCache()` bumps the generation, and a completion from an older generation is not stored.
  - Keep the price entry working through the same structure. Makes T006 pass.
- [X] T009 Add a 10-second in-flight-shared tip-height memo in src/DigiByte.Wallet/Services/BlockchainApiService.cs behind the public `GetBlockHeightAsync()` (line ~280), so every caller shares it: `GetAddressTransactionsAsync` (line ~217), `GetTransactionAsync` (line ~173), `GetAddressHistoryPageAsync`, and the Fallback `blockheight` read that `WalletHistorySync` uses (T019). Makes the first three T007 cases pass.
- [X] T010 Implement `IAddressSummarySource` on `BlockchainApiService` in src/DigiByte.Wallet/Services/BlockchainApiService.cs:
  - Extend the `EsploraAddressCounts` DTO with funded/spent sums for both stats.
  - `GetAddressHistoryPageAsync` maps transactions with the shared tip height and returns the last confirmed txid of the page.
  - Strict: no catch-and-empty. Makes the last two T007 cases pass.
- [X] T011 [P] Write failing tests in tests/DigiByte.Wallet.Tests/AddressSummaryStoreTests.cs:
  - Round-trips `addr_summary_{walletId}_{network}` and `chain_history_{walletId}_{network}`.
  - A record whose `Version` is not 1 is discarded (returns empty).
  - Wallets and networks never share records.
  - `DeleteAsync(walletId)` removes both records for every network.
- [X] T012 Create `AddressSummaryStore` in src/DigiByte.Wallet/Services/AddressSummaryStore.cs over `ISecureStorage`:
  - `AddressSummaryRecord` holds `Version` = 1, `Entries` (address → Summary + Cursor) and `LastFullResyncUtc`.
  - `SavedChainHistory` holds `Version` = 1 and `Transactions` (txid → `TransactionInfo`, plus block height).
  - Methods: load, save, merge-by-txid and delete per wallet. Makes T011 pass.
- [X] T013 Route `IAddressSummarySource` in src/DigiByte.Wallet/Services/FallbackBlockchainService.cs:
  - Use the first explorer that implements it and is not on cooldown, with the same `Note`/cooldown accounting as address reads.
  - Throw `SummaryUnavailableException` on regtest, when no explorer implements it, when all fail, or while the gateway is active in First position (add a test for the gateway-first case).
  - Summary lookups are not cached here; `WalletHistorySync` keeps the summary snapshot.
  - With `recordFailure: false`, a failed lookup records no cooldown or outage, for discovery (T021). `BlockchainApiService` ignores the flag.
  - Add tests for routing, cooldown and unavailable to tests/DigiByte.Wallet.Tests/FallbackCacheTests.cs.
- [X] T014 [P] Fix the post-send invalidation key, which today removes `"cache:balance"` while Home writes `cache:balance:{walletId}`. Remove the active wallet's `cache:balance:{id}`, `cache:transactions:{id}` and `cache:lastFetch:{id}` instead (leave `cache:dgbPrice`; it isn't per wallet), in src/DigiByte.Web/Pages/Send.razor (~line 1208), src/DigiByte.Web/Pages/PsbtSign.razor (~line 537) and src/DigiByte.Web/Pages/Settings.razor (~line 1162).
- [X] T015 Extend `WalletService.DeleteWalletAsync` in src/DigiByte.Wallet/Services/WalletService.cs to also delete `tx_history_{walletId}`, both `AddressSummaryStore` records, and the `cache:balance:`, `cache:transactions:` and `cache:lastFetch:` keys for that wallet id (FR-025). Add a test in tests/DigiByte.Wallet.Tests/AddressSummaryStoreTests.cs that deleting a wallet leaves none of its keys and keeps another wallet's.

**Checkpoint**: Cache sharing, tip memo, summary source, store and cleanup are in place. `dotnet test` is green.

---

## Phase 3: User Story 1 - Refreshing a wallet with no new activity is fast (Priority: P1) 🎯 MVP

**Goal**: A warm refresh with no activity sends one summary per watched address, no history pages and at most one tip height. Pages asking for different history sizes share one fetch per address.

**Independent Test**: With a saved, current summary, refresh against `SummaryExplorer` (80 addresses). Counts: summary = 80, history pages = 0, tip height ≤ 1, and the balance and history equal the previous refresh.

### Tests for User Story 1 ⚠️

- [X] T016 [P] [US1] Failing test in tests/DigiByte.Wallet.Tests/FallbackCacheTests.cs: `GetAddressTransactionsAsync(a, 0, 50)`, then `(a, 0, 500)`, then `(a, 0, 1000)` within the cache lifetime makes one fetch, and each call returns the right slice (FR-003).
- [X] T017 [P] [US1] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - Warm refresh with no activity over 80 addresses gives 80 summary requests, 0 pages and ≤ 1 tip height (SC-001).
  - `GetBalanceAsync()` and `GetTransactionHistoryAsync()` called at the same time request each address's summary once (FR-004).
  - Discovery-read summaries younger than 30 s are reused by the first refresh, with no second summary request (FR-011).
  - With `HistorySyncOptions.ChangeOnly = false`, every address's history is fetched and FR-001–004 still hold (FR-010a).
  - A refresh on regtest or with `SummaryUnavailableException` falls back to today's path with the same results.
  - Saved confirmed transactions gain confirmations when the tip advances between two warm refreshes, and each refresh sends at most one tip request (FR-001, FR-008).

### Implementation for User Story 1

- [X] T018 [US1] Change the history key in src/DigiByte.Wallet/Services/FallbackBlockchainService.cs `GetAddressTransactionsAsync` to `txs:{address}`. The cache holds the full list the source returned, and `skip`/`take` are applied after reading. Makes T016 pass; update any existing test in tests/DigiByte.Wallet.Tests/ParallelQueryTests.cs that asserts the old key behaviour.
- [X] T019 [US1] Create `WalletHistorySync` in src/DigiByte.Wallet/Services/WalletHistorySync.cs per contracts/history-sources.md §4:
  - `RefreshAsync(walletId, network, addresses, ct)` is single-flight per (wallet, network).
  - It keeps an in-memory `SummarySnapshot` (entries younger than 30 s count as current) and reads missing summaries 10 at a time, strict: any failure throws (FR-006).
  - It loads the saved summaries and chain history, picks addresses where `DiffersFrom` is true or no summary or cursor exists, and fetches the newest page for those. Multi-page fetching comes in US2/US3.
  - It merges by txid into `SavedChainHistory` and persists each address's summary only after its merge succeeds (FR-010).
  - Once per refresh, it reads the tip with `IBlockchainService.GetBlockHeightAsync()` (the Fallback cache, 15 s) and recomputes every saved transaction's confirmations from its `BlockHeight` (pending = 0). It never uses the stored confirmation count. If the tip read fails, it keeps the stored counts. The balance does not depend on the tip.
  - It returns `SyncResult(ConfirmedSatoshis, PendingSatoshis, Transactions, AddressesFetched)`.
  - Also implement `Offer(...)`.
- [X] T020 [US1] Wire `WalletHistorySync` into src/DigiByte.Wallet/Services/WalletService.cs. When `HistorySyncOptions.ChangeOnly` is on and the source is an `IAddressSummarySource` (the `FallbackBlockchainService`):
  - `GetBalanceAsync()` uses `RefreshAsync`.
  - `GetTransactionHistoryAsync(skip, take)` uses `RefreshAsync(...).Transactions` in place of the per-address `GetAddressTransactionsAsync` loop (~line 1492), keeping all record-building, merging with `TransactionTracker`, `RepairUnderRecordedBatchesAsync`, the gateway `Offer` and labelling unchanged.
  - On `SummaryUnavailableException`, or when the switch is off, it uses today's path.
  - Check `TryGetWalletHistorySource()` and gateway-first before the change-only path, in the order given in contracts/history-sources.md §5.
- [X] T021 [US1] Seed summaries from discovery in src/DigiByte.Wallet/Services/WalletService.cs `FindUsedCountAsync` (~line 1083). When the source implements `IAddressSummarySource`, call `GetAddressSummaryAsync`, use `IsUsed`, and call `WalletHistorySync.Offer`. On `SummaryUnavailableException`, use `HasAddressHistoryAsync` for that address. Discovery calls `GetAddressSummaryAsync(address, recordFailure: false)`, so a failed discovery lookup never puts an explorer on cooldown or counts toward an outage (same as `HasAddressHistoryAsync` today). Otherwise keep `HasAddressHistoryAsync` (FR-011, research R9). Add tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs: discovery with the gateway first falls back to `HasAddressHistoryAsync`; a failed discovery summary leaves no cooldown.
- [X] T022 [US1] Register `HistorySyncOptions` (singleton), `AddressSummaryStore` and `WalletHistorySync` (scoped) in src/DigiByte.Web/Program.cs and pass them into `WalletService`. At startup, load `ChangeOnly` from `ISecureStorage` key `dev:history-change-only` (missing → true).
- [X] T023 [US1] Add a "Change-only history sync" toggle to src/DigiByte.Web/Pages/DevNode.razor that writes `dev:history-change-only` and updates `HistorySyncOptions.ChangeOnly`, taking effect on the next refresh (FR-010a).

**Checkpoint**: A warm refresh with no activity is cheap. The switch on `/dev/node` returns to today's behaviour. T016–T017 pass.

---

## Phase 4: User Story 2 - New activity is picked up without reloading everything (Priority: P1)

**Goal**: Changed addresses, and only those, get their new transactions. Pending → confirmed, dropped and newly watched addresses are handled. A send refreshes at once. A background full re-read runs once per unlock.

**Independent Test**: From a current saved summary, add one transaction to one address in `SummaryExplorer`. The next refresh pages only that address, shows the transaction, and includes it in the balance.

### Tests for User Story 2 ⚠️

- [X] T024 [P] [US2] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - A new payment on one address pages only that address, with the right amount and direction (SC-003).
  - A pending transaction confirming (total count unchanged, confirmed +1, pending −1) is detected and its confirmations update (US2 #2).
  - A pending transaction that disappears without confirming is removed from saved history and the pending sum.
  - A newly watched address with no saved summary is fetched.
  - New confirmed transactions are fetched page by page until a page contains an already-saved confirmed txid (FR-013).
- [X] T025 [P] [US2] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs for failures:
  - One summary lookup failing makes the balance read throw and leaves every saved summary unchanged (FR-006, SC-006).
  - A history page failing for one changed address still returns the balance, keeps that address's old summary, and retries it next cycle (FR-010).
  - `InvalidateCache()` after a send means the next `GetBalanceAsync()`/`GetTransactionHistoryAsync()` reflect the send, including when a read was in flight (FR-023).
- [X] T026 [P] [US2] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs for the background full re-read (FR-009):
  - It starts after the first change-only result is returned, not before.
  - It waits while a `RefreshAsync` is running.
  - It uses at most two concurrent requests.
  - On success it replaces saved chain history in one write, which corrects a transaction whose block changed with no count change.
  - On failure it changes nothing returned and is retried on the first refresh at least 1 minute after the failure.
  - It runs once per unlock, and locking the wallet cancels it.
  - When the refresh that triggers it already read every watched address in full (complete cursors, nothing left to page), the state goes straight to Done and no extra requests are sent.

### Implementation for User Story 2

- [X] T027 [US2] Extend `WalletHistorySync.RefreshAsync` in src/DigiByte.Wallet/Services/WalletHistorySync.cs:
  - Multi-page fetch for changed addresses that stops at `AddressHistoryCursor.NewestConfirmedTxId`.
  - Replace the address's pending set from the newest page and remove saved pending txids that are neither listed nor confirmed.
  - Update the cursor.
  - Partial page failure keeps that address's old summary and cursor while still returning the summary-based balance. Makes T024–T025 pass.
- [X] T028 [US2] Add `FullResyncState` to the session in src/DigiByte.Wallet/Services/WalletService.cs (`UnlockedWalletSession`) and implement `WalletHistorySync.EnsureFullResync(...)` in src/DigiByte.Wallet/Services/WalletHistorySync.cs per data-model.md. The state machine is NotStarted → Running → Done; a failure goes back to NotStarted and is retried on the first refresh at least 1 minute after the failure.
  - Two requests at a time, waiting while a foreground refresh runs.
  - Full paging per address, then one write.
  - Cancelled on lock or wallet switch.
  - Skip straight to Done when every cursor is `Complete` and was set in this session.
  - Call it from `GetTransactionHistoryAsync` after the change-only result is built. Makes T026 pass.
- [X] T029 [US2] Make sure every send path in src/DigiByte.Wallet/Services/WalletService.cs (single send, batch send, consolidate, PSBT broadcast) calls `InvalidateCache()` on the `FallbackBlockchainService` and drops the `WalletHistorySync` summary snapshot for the wallet after broadcasting (FR-023). Add `WalletHistorySync.ForgetSnapshot(walletId)` if needed.

**Checkpoint**: New activity, confirmations, drops, failures and sends all behave correctly. US1 and US2 together are the change-only sync.

---

## Phase 5: User Story 3 - Busy addresses show their complete history (Priority: P2)

**Goal**: Addresses with more than 25 confirmed transactions show all of them, on every path, multisig included.

**Independent Test**: Give one address 60 confirmed transactions. All 60 appear, and a later refresh with one new transaction fetches one page.

### Tests for User Story 3 ⚠️

- [X] T030 [P] [US3] Failing tests in tests/DigiByte.Wallet.Tests/EsploraPagingTests.cs:
  - `BlockchainApiService.GetAddressTransactionsAsync` on an address with 60 confirmed transactions follows `/txs/chain/{last}` and returns all 60.
  - A source that never ends stops at 2,000 transactions.
  - `skip`/`take` still apply to the full list.
- [X] T031 [P] [US3] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - The first refresh of a 60-transaction address returns the newest page without waiting for older ones; after the background re-read, all 60 are saved and the cursor is `Complete`.
  - One new transaction later fetches exactly one page (SC-004, FR-012/013).
  - Hitting the 2,000 cap sets `Truncated` and logs once.
- [X] T032 [P] [US3] Failing test in tests/DigiByte.Wallet.Tests/MultisigHistoryTests.cs (new): a `MultisigWalletService` over a real `BlockchainApiService` with the stub `HttpMessageHandler` from EsploraPagingTests.cs. A multisig address with 60 confirmed transactions shows all 60 (clarification 4).

### Implementation for User Story 3

- [X] T033 [US3] Make `BlockchainApiService.GetAddressTransactionsAsync` in src/DigiByte.Wallet/Services/BlockchainApiService.cs page through `/address/{a}/txs/chain/{last}` until the end or 2,000 transactions, then apply `skip`/`take`. This serves the change-only-off path and `MultisigWalletService` (~line 1045) without changing their calls. Makes T030 and T032 pass.
- [X] T034 [US3] In src/DigiByte.Wallet/Services/WalletHistorySync.cs, on first load of an address (no cursor): fetch the newest page in the refresh, save it with `Complete = false`, and return. The older pages, up to the 2,000 cap, are fetched by the background full re-read (T028). That re-read sets `Complete`/`Truncated` and `NewestConfirmedTxId`, and a truncation is logged once with no addresses or amounts. The refresh never waits on more than one page per address (spec edge case "Very large address"). Makes T031 pass.

**Checkpoint**: Full history everywhere.

---

## Phase 6: User Story 4 - Incoming payments show as pending straight away (Priority: P2)

**Goal**: The balance reports a net pending amount separately. Home's main figure is confirmed only, with a "Pending ±X DGB" line.

**Independent Test**: An unconfirmed incoming payment shows on the pending line, the main figure and available-to-send exclude it, and it moves into the main figure once confirmed.

### Tests for User Story 4 ⚠️

- [X] T035 [P] [US4] Failing tests in tests/DigiByte.Wallet.Tests/HistorySyncTests.cs:
  - A pending incoming payment gives `UnconfirmedSatoshis` > 0 and `ConfirmedSatoshis` that excludes it (FR-014/015).
  - A pending outgoing payment gives a negative `UnconfirmedSatoshis`.
  - A pending outgoing send of X with fee F makes `AvailableSatoshis` = confirmed − X − F, while `ConfirmedSatoshis` stays unchanged until it confirms (US4 #2).
  - After confirmation, pending is 0 and confirmed includes it.
  - On the full-read path, `UnconfirmedSatoshis` = 0.

### Implementation for User Story 4

- [X] T036 [US4] Set `WalletBalance.UnconfirmedSatoshis` from `SyncResult.PendingSatoshis` (sum of `PendingNet`, "may be negative") in `WalletService.GetBalanceAsync()` in src/DigiByte.Wallet/Services/WalletService.cs, and add `AvailableSatoshis`/`AvailableDgb` (`ConfirmedSatoshis + Math.Min(0, UnconfirmedSatoshis)`) to `WalletBalance` in src/DigiByte.Wallet/Models/WalletInfo.cs. Makes T035 pass.
- [X] T037 [US4] Audit every `TotalDgb` use in src/DigiByte.Web (Home, Send, PsbtSign, Receive, components). Where it means spendable, switch to `AvailableDgb` (including the `ConfirmedDgb` reads at src/DigiByte.Web/Pages/Send.razor:892 and :1024 and src/DigiByte.Web/Pages/PsbtSign.razor:567); where it means the balance shown, use `ConfirmedDgb`. Keep `TotalDgb` meaning confirmed + pending in src/DigiByte.Wallet/Models/WalletInfo.cs.
- [X] T038 [US4] In src/DigiByte.Web/Pages/Home.razor, show `ConfirmedDgb` as the main figure and base the fiat value on it. When `UnconfirmedDgb` ≠ 0, add a separate line "Pending +X DGB" / "Pending −X DGB" that respects `hideBalance`. The balance animation keys on the confirmed figure, so a dropped pending payment doesn't move it (FR-016). Check in the browser preview.

**Checkpoint**: Pending is visible and never spendable.

---

## Phase 7: User Story 5 - Wallet pages show the last known figures immediately (Priority: P2)

**Goal**: Transactions, Receive and Send show the saved snapshot at once and refresh in the background, with "Last updated" when the refresh fails.

**Independent Test**: After one Home load, open each page with a slow network. Saved figures appear within 1 s and are replaced when the fresh answer arrives. Offline, they stay with "Last updated …".

### Tests for User Story 5 ⚠️

- ~~T039~~ Removed: FR-019 is met by WalletSnapshotCache (T041–T044). FR-022/023 stay covered by T006, T025 and T044.

### Implementation for User Story 5

- ~~T040~~ Removed: no stale read is added to `FallbackBlockchainService` (see T039).
- [X] T041 [US5] Create `WalletSnapshotCache` in src/DigiByte.Web/Services/WalletSnapshotCache.cs. It reads, writes and invalidates `cache:balance:{walletId}`, `cache:transactions:{walletId}`, `cache:dgbPrice` and `cache:lastFetch:{walletId}` exactly as Home does today (src/DigiByte.Web/Pages/Home.razor ~lines 616, 830–946), and exposes the last-updated time. Register it in src/DigiByte.Web/Program.cs, then move Home's snapshot code to it, with no change in Home's behaviour. Use it in T014's invalidation spots too.
- [X] T042 [P] [US5] In src/DigiByte.Web/Pages/Transactions.razor, show the snapshot at once (balance and history for the active wallet only), refresh in the background, and on failure keep the snapshot with "Last updated …" (FR-017/018/024).
- [X] T043 [P] [US5] In src/DigiByte.Web/Pages/Receive.razor, show the snapshot history at once in place of waiting on `GetTransactionHistoryAsync(0, 1000)` (~line 413), refreshing in the background; same failure rule.
- [X] T044 [P] [US5] In src/DigiByte.Web/Pages/Send.razor, show the page snapshot (`WalletSnapshotCache`) balance at once as the displayed available amount, refreshing in the background. The coins for the send are still read fresh at confirm time with no change to the signing path (FR-022), and the page snapshot is invalidated after a send.
- [X] T045 [US5] Check src/DigiByte.Web/Pages/Transactions.razor, Receive.razor and Send.razor in the browser preview with DevTools throttling: each page shows saved data within 1 s; offline shows "Last updated …"; switching wallets never shows another wallet's figures (SC-005, US5 #3).

**Checkpoint**: Every wallet page feels instant.

---

## Phase 8: User Story 6 - Gateway users get history without heavy processing (Priority: P3)

**Goal**: With the gateway answering history, the wallet's history is built in one pass over the saved record.

**Independent Test**: With 80 addresses and 200 transactions, each transaction is parsed once, and the result equals the per-address result.

### Tests for User Story 6 ⚠️

- [X] T046 [P] [US6] Failing tests in tests/DigiByte.Wallet.Tests/GatewayWalletHistoryTests.cs, using the gateway doubles from tests/DigiByte.Wallet.Tests/P2pGatewayBlockchainServiceTests.cs:
  - `GetWalletTransactionsAsync(80 addresses)` over 200 transactions equals the union of per-address `GetAddressTransactionsAsync` results (txids, inputs, outputs, confirmations) (FR-021).
  - Each raw transaction is parsed once (SC-007).
  - `CannotAnswer` still throws as the per-address method does.
  - `FallbackBlockchainService.TryGetWalletHistorySource()` returns the gateway only while it would answer history.
  - With the gateway First and change-only on, no summary or history request reaches the explorer double.

### Implementation for User Story 6

- [X] T047 [US6] Implement `IWalletHistorySource.GetWalletTransactionsAsync` on src/DigiByte.Wallet/Services/P2pGatewayBlockchainService.cs: work out each address's script once, parse each `SyncTransaction.Raw` once, keep the ones that pay or spend any script, and order them as `GetAddressTransactionsAsync` does.
- [X] T048 [US6] Add `TryGetWalletHistorySource()` to src/DigiByte.Wallet/Services/FallbackBlockchainService.cs (gateway active and in First position, or Backup with every explorer failed) and use it in `WalletService.GetTransactionHistoryAsync` in src/DigiByte.Wallet/Services/WalletService.cs before the per-address loop. Verification labelling stays unchanged. Makes T046 pass.

**Checkpoint**: All six stories work.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T049 Create `RequestCountingHandler` in src/DigiByte.Web/Services/RequestCountingHandler.cs: a `DelegatingHandler` that counts only while a session is open. It groups requests as `/address/{a}` = Summary, `/address/{a}/txs…` = HistoryPage, `/blocks/tip/height` = TipHeight, and anything else = Other. Counts are in memory only, never logged, saved or sent (FR-010b). Register it on the explorer `HttpClient` in src/DigiByte.Web/Program.cs.
- [X] T050 Add a "Wallet refresh benchmark" to src/DigiByte.Web/Pages/DevNode.razor (needs an unlocked wallet). It runs balance plus history four times: on/cold, on/warm, off/cold, off/warm, clearing the Fallback cache, the summary snapshot, the page snapshot (`WalletSnapshotCache`) and saved summaries for the cold runs. Before each run, cancel any running full re-read and set `FullResyncState` to Done for the session, so it can't add requests to a later run; counting starts only after the cancel has finished. After the last run, set it back to NotStarted. For each run it shows the time and the Summary, HistoryPage, TipHeight and Other counts, then puts the switch back to its previous value.
- [X] T051 [P] Update docs/ARCHITECTURE.md (read path: address summaries, change-only sync, saved chain history, background full re-read, `/dev/node` switch) and docs/PROCESS_FLOWS.md if it describes balance or history refresh.
- [X] T052 Run `dotnet build` and `dotnet test` for the whole solution; fix anything this feature broke in existing tests (e.g. tests/DigiByte.Wallet.Tests/TransactionHistoryAddressTests.cs, GatewayWiringTests.cs, ParallelQueryTests.cs).
- [X] T053 Run quickstart.md sections 2 and 3 against the live explorer in the browser preview. Record the `/dev/node` on/off warm times and counts in the PR description as proof of SC-001 and SC-002.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none. T000a and T002–T005 can run in parallel; T001 follows T000a.
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

- Phase 1: T000a, T002–T005 (then T001).
- Phase 2: T006, T007 and T011 (tests in different files), and T014 (Web pages) next to any wallet task.
- After Phase 2: US5 (T041–T045) and US6 (T046–T048) can run alongside US1, because they touch different files. The exception is that T048 also touches `FallbackBlockchainService.cs`, so run it after T018.
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
