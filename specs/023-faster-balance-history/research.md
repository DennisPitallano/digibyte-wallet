# Research: Faster Balance and History

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-21

All Technical Context unknowns are resolved below. Each item gives the decision, why, and what else was considered.

## R1. Where the requests go today

**Finding** (measured by reading the code, typical seed wallet, ~80 watched addresses from `WalletService.GetAllAddresses`):

| Step | Code | Requests |
|------|------|----------|
| Balance | `WalletService.GetBalanceAsync` → `FallbackBlockchainService.GetBalanceAsync(list)` → `BlockchainApiService.GetBalanceStrictAsync` | 1 × `GET /address/{a}` per address |
| History | `WalletService.GetTransactionHistoryAsync` → per address `FallbackBlockchainService.GetAddressTransactionsAsync` → `BlockchainApiService.GetAddressTransactionsAsync` | 1 × `GET /address/{a}/txs` per address |
| Tip height inside history | `BlockchainApiService.cs:217` calls its own `GetBlockHeightAsync()`, which skips the Fallback `blockheight` cache | 1 × `GET /blocks/tip/height` per address |
| Discovery | `FindUsedCountAsync` → `HasAddressHistoryAsync` | 1 × `GET /address/{a}` per looked-up address, answer reduced to a bool |

Home runs balance and history together, so one refresh is about 3 × 80 = 240 requests. Discovery repeats the `/address/{a}` lookups and throws away the amounts.

Other findings that shape the design:

- `CachedRead` (`FallbackBlockchainService.cs:773`) saves finished results only, so concurrent misses each go to the network.
- The history key is `txs:{address}:{skip}:{take}`. Callers ask for 50 (Home), 500 (Send, PsbtSign), 1000 (Receive) and `MaxLoaded` (Transactions), so the same address can be fetched four times. Esplora returns the same page whatever `take` is; the slicing is done on our side.
- Esplora `GET /address/{a}/txs` returns up to 50 mempool transactions plus the newest 25 confirmed ones. Older ones need `GET /address/{a}/txs/chain/{last_seen_txid}` (25 per page). Today nothing pages, so history stops at 25 confirmed per address.
- `GetBalanceStrictAsync` reads `chain_stats` only and ignores `mempool_stats`. `WalletBalance.UnconfirmedSatoshis` exists but is never set, and `TotalDgb` adds it into the total.
- `P2pGatewayBlockchainService.GetAddressTransactionsAsync` parses every saved raw transaction and calls `Describe` for each address. With 80 addresses and 200 transactions, one load does about 16,000 parses.
- Wallet deletion (`WalletService.DeleteWalletAsync`) removes the gateway records but not `tx_history_{id}` or Home's `cache:*:{id}` keys.
- After a send, `Send.razor:1208`, `PsbtSign.razor:537` and `Settings.razor:1162` remove `cache:balance`, but Home writes `cache:balance:{walletId}`. The removal never hits the real key, so this invalidation does nothing today.

## R2. Address summary source

**Decision**: Add an optional interface `IAddressSummarySource` (see [contracts/history-sources.md](contracts/history-sources.md)) implemented by `BlockchainApiService` (Esplora) and by `FallbackBlockchainService`, which routes it to the first explorer that implements it, with the existing cooldowns and failure accounting. The own node and the gateway don't implement it. When no source can give a summary, `WalletService` uses today's full-read path for that refresh.

**Rationale**: `/address/{a}` already returns everything the summary needs, in a single request: `chain_stats` and `mempool_stats`, each with `tx_count`, `funded_txo_sum` and `spent_txo_sum`. An optional interface keeps `IBlockchainService` unchanged for its ten implementations and test doubles.

**Alternatives considered**: Adding the method to `IBlockchainService` would touch every implementation and test double. Building summaries from history would need the history first, which defeats the purpose. Blockbook can also give counts, but no Blockbook instance is configured (removed 2026-11-16). It can implement the interface later.

## R3. Detecting change

**Decision**: An address needs its history fetched when any of these differs from the saved summary: `ConfirmedTxCount`, `PendingTxCount`, the confirmed amounts or the pending amounts. It also needs it when there is no saved summary, or when the address's history cursor is missing.

**Rationale**: When a pending transaction confirms, the total count stays the same while the confirmed count goes up and the pending count goes down, so the two counts have to be compared separately (spec edge case). Comparing the amounts too costs nothing and catches odd source answers.

**Alternatives considered**: Electrum-style status hashes need an Electrum server (out of scope). Comparing the total `tx_count` alone misses pending → confirmed.

## R4. Fetching only new transactions and complete history

**Decision**: For a changed address, page newest first: `GET /address/{a}/txs`, then `GET /address/{a}/txs/chain/{last}` while every confirmed transaction on the page is unknown. Stop at the first page that contains an already-saved confirmed txid, or at the end of the history. On first load the refresh takes the newest page only; the background full re-read (R8) pages to the end, so a busy address never delays the balance or history. There's a safety cap of 2,000 transactions per address, logged when hit, after which the address is marked `Truncated`.

Pending transactions on the first page replace the saved pending set for that address. A saved pending transaction no longer listed and not confirmed is marked dropped and removed from the saved history.

The legacy (change-only off) path and multisig use the same pager, so complete history (FR-012) doesn't depend on the switch.

**Rationale**: This is how FR-012 and FR-013 come out of one pager. The cap stops a faulty source from making the wallet page forever, matching the `MaxAddressSearch` guard used by discovery.

**Alternatives considered**: Re-reading full history on every change is simple but defeats FR-013. A fixed page count would silently truncate, which is the bug we're fixing.

## R5. Sharing in-flight reads and the cache key

**Decision**: `FallbackBlockchainService` keeps `ConcurrentDictionary<string, CacheEntry>`, where an entry holds either a finished value with an expiry or a running `Task<object>`. A miss stores the running task, so concurrent callers await it. A failed task is removed, never cached. History is cached as the address's full list under `txs:{address}`, and `skip`/`take` are applied after reading. `InvalidateCache()` also drops running entries, so a read started before a send can't fill the cache afterwards: each entry carries a generation number, and a completion from an older generation is not stored.

**Rationale**: This covers FR-002 and FR-003, and the generation number keeps FR-023 true while reads are in flight.

**Alternatives considered**: `Lazy<Task>` behaves the same but is harder to evict on failure. A per-key `SemaphoreSlim` adds more allocations and is easy to deadlock.

## R6. One tip height per load

**Decision**: `BlockchainApiService` keeps its own height in memory for 10 seconds, sharing the in-flight request, and uses it inside `GetAddressTransactionsAsync` and `GetTransactionAsync`. The Fallback `blockheight` cache stays as it is.

**Rationale**: The inner service is also constructed directly (DevNode benchmark, `InvoiceMonitor` in Pay.Api), so the fix has to live where the request is made. Ten seconds is shorter than the Fallback's 15, so the confirmation counts never lag what the Fallback reports.

**Alternatives considered**: Passing the height in from the caller would change the `IBlockchainService` signature everywhere.

## R7. Saved data layout

**Decision**: Two new records in `ISecureStorage`, keyed per wallet and network:

- `addr_summary_{walletId}_{network}`: a map from address to summary, plus that address's history cursor.
- `chain_history_{walletId}_{network}`: a map from txid to `TransactionInfo`, the same explorer-shaped data `GetTransactionHistoryAsync` already builds records from.

`TransactionTracker` (`tx_history_{walletId}`) stays the store for the wallet's own sends and receives and keeps winning on duplicates, as today. Records keep being built from `TransactionInfo` on each read, using the current address set, change keys and resolver, so labels stay right when the address set grows. Each record carries a `version` field; an unknown version is discarded and followed by a full read.

**Rationale**: Saving the explorer-shaped data keeps the existing record-building code (direction, amount, counterparty, DigiDollar, memo) as the single source of truth. Clarification 2 says use the same storage and protection as the Home cache: `ISecureStorage`, backed by IndexedDB, with no added encryption.

**Alternatives considered**: Saving `TransactionRecord`s directly would freeze labels computed with an older address set. One key per transaction would be a lot of IndexedDB churn.

**Size**: about 1–2 KB per transaction as JSON; 1,000 transactions ≈ 1–2 MB per wallet, well within IndexedDB quota.

## R8. The full re-read once per unlock

**Decision**: `UnlockedWalletSession` gets a `FullResyncState` (NotStarted, Running, Done). After the first change-only history result has been returned, `WalletHistorySync` starts a background full re-read: every watched address, full paging, two requests at a time. Before each address it waits while any foreground refresh is running, which gives way to the user's reads (FR-009). When it finishes, it replaces the saved chain history in one write. If it fails, the state goes back to NotStarted and it is retried on the first refresh at least 1 minute after the failure; nothing on screen changes. Locking the wallet cancels it.

**Rationale**: This follows clarification 1, and the same retry delay is already used by discovery (`AddressDiscoveryRetryDelay`).

**Alternatives considered**: A Web Worker is more complex, and Blazor WASM is single-threaded anyway; `Task`-based waiting is enough to give way to the user's reads.

## R9. Discovery seeds the summaries

**Decision**: When the source implements `IAddressSummarySource`, `FindUsedCountAsync` calls `GetAddressSummaryAsync` and derives "used" from the counts (confirmed + pending > 0, the same rule as `HasAddressHistoryAsync`). The summaries go into a per-session in-memory snapshot stamped with the read time. A balance or history read reuses snapshot entries younger than the balance cache lifetime (30 s) and reads the rest fresh. Snapshot entries are persisted only after the history step succeeds (FR-010).

**Rationale**: FR-011 says don't read the same addresses twice. FR-006's "read during this refresh" is met because the entries come from a live read seconds earlier, never from storage.

**Alternatives considered**: Leaving discovery as is would mean about 40 duplicate requests per unlock.

## R10. Gateway one-pass history

**Decision**: An optional interface `IWalletHistorySource.GetWalletTransactionsAsync(IReadOnlyList<string> addresses)` on `P2pGatewayBlockchainService`. It works out every address's script once, parses each saved transaction once, and keeps the ones that pay or spend any of those scripts. `FallbackBlockchainService` exposes it only while the gateway would answer history: gateway active, either in First position or in Backup position after every explorer has failed. Otherwise the per-address path runs as today. `WalletService` checks it first, before change-only sync, and summary routing refuses while the gateway is First, so a gateway-first wallet's balance and history never go to the explorer (contracts §5).

**Rationale**: This covers FR-020. FR-021 (same output) is tested by comparing it with the per-address result on the same record.

## R11. The change-only switch and request counts

**Decision**:
- `HistorySyncOptions { bool ChangeOnly = true }` is a singleton in `DigiByte.Wallet`. The Web layer loads it at startup from the `ISecureStorage` key `dev:history-change-only` and writes it from a toggle on `/dev/node`. `WalletService` reads it on each refresh, so a change takes effect on the next refresh (FR-010a).
- Request counts (FR-010b) come from a `RequestCountingHandler`, a `DelegatingHandler` in `DigiByte.Web` registered on the explorer `HttpClient`. It counts only while a benchmark session is open, groups requests by URL shape (`/address/{a}` = summary, `/address/{a}/txs…` = history page, `/blocks/tip/height` = height) and keeps everything in memory. The new "Wallet refresh" benchmark runs the active wallet's balance plus history twice (cold, then warm), with the switch on and then off, and shows time and counts for each run.

**Rationale**: Counting in the HTTP pipeline measures what really goes over the network. It's a Web concern, so `DigiByte.Wallet` gets no diagnostics dependency.

## R12. Instant display on other pages

**Decision**: Move Home's saved-snapshot read and write (`cache:balance:{id}`, `cache:transactions:{id}`, `cache:lastFetch:{id}`, plus the shared `cache:dgbPrice`) into a `WalletSnapshotCache` service in `DigiByte.Web`. Home, Transactions, Receive and Send all use it: show the snapshot at once, then refresh. A failed refresh keeps the snapshot and shows "Last updated …" (FR-018). Send keeps reading the coins fresh at confirm time (FR-022). The snapshot is invalidated by wallet id after a send (fixing the key bug from R1), and removed when the wallet is deleted.

The page snapshot is what meets FR-019; `FallbackBlockchainService` gets no stale read. Coin reads are never cached, and the generation number from R5 stops anything from before a send being cached afterwards.

## R13. Pending figure on Home

**Decision**: `WalletBalance.UnconfirmedSatoshis` = the sum over current summaries of (pending funded − pending spent). It can be negative. Home's main figure and fiat value switch from `TotalDgb` to `ConfirmedDgb`, and a "Pending ±X DGB" line shows when the value isn't zero (FR-016). Every `TotalDgb` use gets audited: places that mean the shown balance move to `ConfirmedDgb`, and `TotalDgb` keeps meaning confirmed + pending. Send and PsbtSign use `ConfirmedDgb` today; they move to a new derived `AvailableDgb` = confirmed + min(0, pending). Confirmed figures only count a spend once it confirms, so without this a pending send's coins would still be offered (FR-015). The coins actually picked are still read fresh at send time.

**Rationale**: This follows clarification 5 and keeps the meaning of the existing property.
