# Contract: History Sources and Sync

Internal contracts between `DigiByte.Wallet` services, plus the explorer endpoints they depend on. Signatures are given as C# outlines. Bodies belong to implementation.

## 1. Explorer endpoints used (Esplora, digiexplorer.info)

| Purpose | Request | Fields read |
|---------|---------|-------------|
| Address summary | `GET /address/{a}` | `chain_stats.{tx_count, funded_txo_sum, spent_txo_sum}`, `mempool_stats.{tx_count, funded_txo_sum, spent_txo_sum}` |
| Newest page (pending + newest 25 confirmed) | `GET /address/{a}/txs` | existing `EsploraTx` |
| Older confirmed page (25) | `GET /address/{a}/txs/chain/{last_seen_txid}` | existing `EsploraTx` |
| Tip height | `GET /blocks/tip/height` | integer body |

If `chain_stats` or `mempool_stats` is missing, or the reply is not 2xx, the lookup fails (an exception), never an empty summary.

## 2. `IAddressSummarySource` (new, optional)

```csharp
public interface IAddressSummarySource
{
    /// One lookup. Throws on any failure or incomplete reply (never returns an empty summary).
    Task<AddressSummary> GetAddressSummaryAsync(string address);

    /// Newest-first page of an address's history. afterTxId null = newest page (includes pending);
    /// otherwise the next 25 confirmed after that txid. Empty list = end of history. Throws on failure.
    Task<AddressHistoryPage> GetAddressHistoryPageAsync(string address, string? afterTxId);
}

public sealed record AddressHistoryPage(IReadOnlyList<TransactionInfo> Transactions, string? LastConfirmedTxId);
```

Implemented by:
- `BlockchainApiService`: the endpoints above. Its tip height is kept in memory for 10 s with in-flight sharing (R6).
- `FallbackBlockchainService`: routes to the first explorer that implements it and is not on cooldown, with the same cooldown and counter rules as other address reads. Throws `SummaryUnavailableException` when none can answer: it isn't regtest, no explorer implements it, or all failed. The caller then uses the full-read path. Also throws `SummaryUnavailableException` while the gateway is active in First position, so a gateway-first wallet never has its addresses looked up through the explorer for balance or history. Discovery uses a route that records no cooldown or outage on failure, matching `HasAddressHistoryAsync`; on `SummaryUnavailableException` discovery uses `HasAddressHistoryAsync`.

Not implemented by `NodeApiBlockchainService`, `P2pGatewayBlockchainService` or `MockBlockchainService`. Test doubles may implement it.

## 3. `IWalletHistorySource` (new, optional)

```csharp
public interface IWalletHistorySource
{
    /// All transactions paying or spending any of the given addresses, each processed once. Throws when it cannot answer.
    Task<List<TransactionInfo>> GetWalletTransactionsAsync(IReadOnlyList<string> addresses);
}
```

Implemented by `P2pGatewayBlockchainService` (one pass over the record). `FallbackBlockchainService` exposes it through `TryGetWalletHistorySource()`, which returns the gateway only while the gateway would answer history reads (R10); otherwise null.

Guarantee (FR-021): for any record and address set, the result equals the union of the per-address `GetAddressTransactionsAsync` results: same txids, inputs, outputs, confirmations.

## 4. `WalletHistorySync` (new)

```csharp
public sealed class WalletHistorySync
{
    /// One refresh cycle for the given addresses. Single-flight per (wallet, network): concurrent callers share one run.
    /// Reuses snapshot summaries younger than 30 s; reads the rest (10 at a time, strict).
    /// Throws if any summary lookup fails (balance must fail, FR-006).
    Task<SyncResult> RefreshAsync(string walletId, string network, IReadOnlyList<string> addresses, CancellationToken ct);

    /// Seeds the in-memory snapshot from discovery reads (FR-011). Not persisted until a refresh succeeds.
    void Offer(string walletId, string network, AddressSummary summary);

    /// Starts the once-per-unlock background full re-read if not started (FR-009). Never throws; never blocks.
    void EnsureFullResync(string walletId, string network, IReadOnlyList<string> addresses);

    /// Drops snapshot, saved summaries and saved chain history for a wallet (delete, FR-025).
    Task ForgetAsync(string walletId);
}

public sealed record SyncResult(
    long ConfirmedSatoshis,
    long PendingSatoshis,
    IReadOnlyCollection<TransactionInfo> Transactions,  // saved + newly fetched, merged
    int AddressesFetched);                               // how many needed history this cycle
```

Rules:
- Confirmations of saved transactions are recomputed from their block height and one tip read per refresh (`GetBlockHeightAsync()`).
- Only changed or unknown addresses get history pages (R3, R4). Summaries are persisted per address only after its history merge succeeds (FR-010).
- If history paging fails for some addresses, the balance from the summaries is still returned. Those addresses keep their old summary, so they are retried next cycle. The history returned is the saved history plus what succeeded.
- Background full re-read: two requests at a time, waits while a `RefreshAsync` is running, replaces saved chain history in one write, never changes what was already returned.

## 5. `WalletService` behaviour (changed)

| Method | Change-only on and summaries available (or source order) | Otherwise |
|--------|----------------------------------------------------------|-----------|
| `GetBalanceAsync()` | Order: (1) gateway active and First → today's path (gateway coins); (2) change-only on and summaries available → `RefreshAsync` → `ConfirmedSatoshis`, `UnconfirmedSatoshis`; (3) otherwise today's `GetBalanceAsync(addresses)`. `UnconfirmedSatoshis` = 0 outside (2) | — |
| `GetTransactionHistoryAsync(skip, take)` | Order: (1) `TryGetWalletHistorySource()` not null → one-pass gateway history; (2) change-only on and summaries available → `RefreshAsync` (shared with the balance call) → build records from `Transactions`, then `EnsureFullResync`; (3) otherwise per-address reads as today (shared cache, one height, complete paging) | — |
| `FindUsedCountAsync` (discovery) | Summary lookup → `IsUsed`, and `Offer` | `HasAddressHistoryAsync` as today |
| After a send | `InvalidateCache()` (bumps generation) + snapshot dropped | same |
| `DeleteWalletAsync` | + `ForgetAsync`, + remove `tx_history_{id}` | same |

## 6. `/dev/node` (Web)

- **Toggle** "Change-only history sync" (default on), saved under `dev:history-change-only`; takes effect on the next refresh.
- **Wallet refresh benchmark** (needs an unlocked wallet). Runs: switch on/cold, on/warm, off/cold, off/warm. For each run it shows the total time and the counts of summary, history-page, tip-height and other requests. Counting is on only while it runs. Results live in memory only.
