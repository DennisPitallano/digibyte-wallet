# Research: Fewer Requests per Refresh and per Send

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Where the requests go today (0.7.0)

| Path | Code | Requests |
|------|------|----------|
| Automatic refresh (Home timer, page opens) | `WalletHistorySync.RunRefreshAsync` reuses a summary only if read < 30 s ago (`SnapshotLifetime`), otherwise reads it | 1 summary per watched address (~60) on almost every poll |
| Manual refresh (Home button and pull gesture) | `Home.ManualRefreshAsync` → same `GetBalanceAsync` / `GetTransactionHistoryAsync` | same as automatic; within 30 s of the last refresh it reads nothing at all |
| Activity pull to refresh | `Transactions.razor` `PullToRefresh OnRefresh="LoadAsync"` | same |
| Send / batch send / consolidate | `WalletService.SendAsync` (~line 1286), `SendBatchAsync` (~1457), `ConsolidateUtxosAsync` (~1949): `GetUtxosAsync` for every entry of `GetAddressKeyMap()` | ~60 coin reads |

## R1. Which addresses are hot

**Decision**: A pure, unit-tested `HotAddressSelector` in `DigiByte.Wallet` decides the hot set from inputs that are all already in memory:

- latest summaries in the session snapshot: `Confirmed > 0`, or any pending count or amount;
- hints from `WalletService`: the receive address on show (`NextReceivingIndex`, SegWit), the 3 newest handed-out receive addresses below it that have no activity, the next change address (`NextChangeIndex`, SegWit), and the wallet's own addresses spent from by a pending send (inputs of pending transactions in the saved history that belong to the wallet);
- recent activity: addresses appearing in a saved transaction whose timestamp is within 7 days (from `SavedChainHistory`, which the sync already keeps in memory).

**Rationale**: Every input is local, so choosing the hot set costs no request. Keeping the rule in a pure function makes FR-002 directly testable.

**Alternatives considered**: Keeping every used address hot. Rejected: a long-lived wallet can have dozens of emptied used addresses, which would eat most of the saving. Learning per-address payment frequency: more complex for little gain.

## R2. Cold checks spread over 5 minutes

**Decision**: Each cold address records when it was last read live (the existing snapshot stamp). In an automatic refresh a cold address is read when its stamp is ≥ 5 minutes old, and additionally the oldest `ceil(coldCount × elapsed / 5 min)` cold addresses are read, where `elapsed` is the time since the previous refresh of that wallet (capped at 5 minutes). Hot addresses keep the 30-second reuse from spec 023.

**Rationale**: After a full refresh every cold stamp is the same, so without spreading they would all expire together and cause a 60-request burst every 5 minutes (FR-009). Reading the oldest share in proportion to elapsed time gives a steady trickle (about 12 a minute for 60 cold addresses) whatever the poll pace, and still guarantees the 5-minute bound.

**Alternatives considered**: A fixed number per refresh. That breaks the bound when the poll backs off to 60 s. A separate background timer is another moving part that keeps running while the page is hidden.

## R3. What counts as a full check

**Decision**: A full check is a refresh after the snapshot for the wallet was dropped. `WalletService.RequestFullCheck()` drops it (through `WalletHistorySync.ForgetSnapshot`, already used after sends). Callers: Home `ManualRefreshAsync` (button and pull gesture), Activity's pull to refresh. Unlock already resets the session (empty snapshot); a send already drops it.

**Rationale**: Reuses the existing reset paths, so "first after unlock" and "first after a send" need no new code, and a full check is exactly "nothing in the snapshot is current".

**Alternatives considered**: A `full` flag on every read method. It would thread through balance, history and both caches for the same effect.

## R4. Reuse rule and the balance floor

**Decision**: The sync reuses a snapshot summary if it is hot and < 30 s old, or cold and < 5 min old (and not chosen by R2). A summary loaded from storage is never reused (unchanged from spec 023). A failed read fails the refresh (unchanged). The balance is the sum of the summaries used, so it can never be lower than the sum of the latest summaries (FR-007).

**Rationale**: The only new freshness allowance is the 5-minute window for cold addresses, which is exactly the delay the spec accepts.

## R5. "Fully checked" time

**Decision**: `WalletHistorySync.GetFullyCheckedAt(walletId, network, addresses)` returns the oldest snapshot stamp across the watched addresses (null if any is missing). Home's wallet info panel shows "Fully checked N min ago". With tiering off every refresh reads everything, so it is simply the last refresh time.

## R6. Coins only where there is money

**Decision**: A `SendCoinPlanner` (pure, in `DigiByte.Wallet`) splits the address-key map into `Funded` (latest summary shows `Confirmed > 0` or any pending activity) and `Rest`, when every watched address has a snapshot summary within its reuse window; otherwise everything is `Funded` (today's behaviour). `SendAsync`, `SendBatchAsync` and `ConsolidateUtxosAsync` read coins for `Funded` first (fresh, as today). If the coins found are fewer than the amount plus a fee allowance (0.01 DGB, well above any realistic DGB fee), they read `Rest` too and continue as today, so "Insufficient funds" is reported only after every address was read (FR-010). Consolidate always needs every coin, so it reads `Funded` only when the summaries are current and the rest only if a summary disagrees with what was found.

**Rationale**: The decision uses only data already in memory, and the fallback keeps today's exact behaviour whenever summaries are missing or wrong. Coins are still never cached (FR-015).

**Alternatives considered**: Reading fresh summaries for every address before a send. That costs ~60 requests again and defeats the purpose.

## R7. Switch and benchmark

**Decision**: `HistorySyncOptions.Tiered` (default true), stored under `dev:tiered-refresh`, toggled by a second switch on `/dev/node`. Tiering applies only when `ChangeOnly` is also on (clarification 4). The benchmark gains two runs, "Tiered on, quiet" and "Tiered off, quiet": both start from a full check, then mark the snapshot 31 seconds old with `WalletHistorySync.AgeSnapshot` (a diagnostic method), so hot addresses are due and cold ones are not, and refresh once. It also shows "Coins read by a send" from `WalletService.PlanSendCoinReads()` (counts only, no reads).

**Rationale**: The benchmark can't wait 31 seconds between runs in a useful way, and real time can't be faked in the browser; aging the stamps reproduces a quiet refresh exactly.

## R8. Multisig, single-key and small wallets

**Decision**: Tiering is skipped when a wallet watches fewer than 10 addresses (FR-016). Multisig wallets don't go through `WalletHistorySync` at all and are unaffected.
