# Data Model: Faster Balance and History

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

Everything below lives in `DigiByte.Wallet` unless marked **Web**. Everything that is saved goes in `ISecureStorage` (IndexedDB in the app), separately per wallet and network, and is removed when the wallet is deleted (FR-025).

## AddressSummary (new, saved)

What the source last reported for one watched address.

| Field | Type | Rule |
|-------|------|------|
| `Address` | string | Key, exactly as derived (not normalised) |
| `ConfirmedTxCount` | int | ≥ 0 |
| `PendingTxCount` | int | ≥ 0 |
| `ConfirmedFunded` | long sat | ≥ 0 |
| `ConfirmedSpent` | long sat | 0 ≤ value ≤ `ConfirmedFunded` |
| `PendingFunded` | long sat | ≥ 0 |
| `PendingSpent` | long sat | ≥ 0 |
| `ReadAtUtc` | DateTime | When the source answered |

Derived values:
- `Confirmed` = `ConfirmedFunded − ConfirmedSpent`
- `PendingNet` = `PendingFunded − PendingSpent` (may be negative)
- `IsUsed` = `ConfirmedTxCount + PendingTxCount > 0`, the same rule as `HasAddressHistoryAsync`
- `DiffersFrom(other)`: true if any count or amount field differs (research R3)

A reply with missing `chain_stats` or `mempool_stats` is a failed lookup. It is never treated as an empty summary (FR-006).

## AddressHistoryCursor (new, saved with the summary)

| Field | Type | Meaning |
|-------|------|---------|
| `Complete` | bool | Every confirmed transaction up to the summary's count has been fetched |
| `Truncated` | bool | The 2,000-transaction safety cap was hit (R4) |
| `NewestConfirmedTxId` | string? | Newest confirmed txid saved for this address; later fetches stop at it (FR-013) |
| `PendingTxIds` | string[] | Pending txids last seen for this address |

## AddressSummaryRecord (new, saved)

Key: `addr_summary_{walletId}_{network}`

| Field | Type |
|-------|------|
| `Version` | int (1). Any other value → discard and do a full read |
| `Entries` | map address → { `Summary`, `Cursor` } |
| `LastFullResyncUtc` | DateTime? (diagnostic only) |

## SavedChainHistory (new, saved)

Key: `chain_history_{walletId}_{network}`

| Field | Type |
|-------|------|
| `Version` | int (1) |
| `Transactions` | map txid → `TransactionInfo` (existing explorer-shaped model: confirmations/height, timestamp, fee, inputs, outputs) |
| `BlockHeight` | int? per transaction (needed to recompute confirmations from the tip) |

Rules:
- Merge (FR-008): a txid already saved is replaced by the newer answer, never duplicated.
- A pending txid that is no longer reported and has not confirmed is removed (dropped or replaced edge case).
- The full re-read (FR-009) replaces `Transactions` for all watched addresses in one write.
- Confirmations are recomputed on every refresh from `BlockHeight` and one tip read; the stored confirmation count is never used. If the tip read fails, the stored counts are kept.
- `TransactionRecord`s are still built from these on each read, with today's resolver. `TransactionTracker` (`tx_history_{walletId}`) still wins on duplicates.

## WalletBalance (existing, changed meaning of one field)

| Field | Change |
|-------|--------|
| `ConfirmedSatoshis` | Sum of `Confirmed` over current summaries (or today's source total on the full-read path) |
| `UnconfirmedSatoshis` | **Now set**: sum of `PendingNet`, may be negative. 0 on sources without summaries |
| `ConfirmedDgb` | The main figure on Home and the base of its fiat value (FR-016) |
| `TotalDgb` | Unchanged formula (confirmed + pending); no longer used as Home's main figure |
| `AvailableSatoshis` / `AvailableDgb` | **New, derived**: `ConfirmedSatoshis + Math.Min(0, UnconfirmedSatoshis)`, what Send offers (FR-015). Equals confirmed when there are no summaries |

## SummarySnapshot (new, in memory per unlocked session)

The summaries read during the current refresh cycle: an address → `AddressSummary` map with read times. Entries younger than 30 s count as current (FR-006, FR-011). Not saved directly. Its entries are written to `AddressSummaryRecord` only after the address's history step succeeds (FR-010).

## FullResyncState (new, in memory per unlocked session)

```text
NotStarted ──all cursors completed this session─────────────────▶ Done
NotStarted ──first change-only result returned──▶ Running ──success──▶ Done
    ▲                                               │
    └── failure (retried on first refresh ≥ 1 min later) ◀┘
Lock / switch wallet: Running → cancelled → NotStarted (next unlock starts again)
```

## HistorySyncOptions (new, singleton)

| Field | Default | Source |
|-------|---------|--------|
| `ChangeOnly` | true | **Web** loads and saves it under `dev:history-change-only` (per device), toggled on `/dev/node` (FR-010a) |

## CacheEntry (changed, inside FallbackBlockchainService)

| Field | Meaning |
|-------|---------|
| `Value` / `ExpiresAt` | Finished answer |
| `Pending` | `Task<object>` while in flight (shared by concurrent callers, FR-002) |
| `Generation` | Cache generation when the read started; a completion from an older generation is not stored (FR-023) |

History key becomes `txs:{address}` (full list); `skip`/`take` are applied after the read (FR-003).

## WalletSnapshot (Web, existing keys, new shared service)

Keys `cache:balance:{walletId}`, `cache:transactions:{walletId}`, `cache:lastFetch:{walletId}` and `cache:dgbPrice` (shared by all wallets), exactly as Home writes them today (`Home.razor:613–618`). They are now read and written through `WalletSnapshotCache` by Home, Transactions, Receive and Send. They are removed after a send (by wallet id) and when the wallet is deleted.

## RequestCounts (Web, in memory only)

Counts per benchmark run: `Summary`, `HistoryPage`, `TipHeight`, `Other`. Never saved, logged or sent (FR-010b).
