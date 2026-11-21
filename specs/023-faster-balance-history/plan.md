# Implementation Plan: Faster Balance and History

**Branch**: `023-faster-balance-history` (spec directory; the git branch is the user's choice, e.g. `feature/faster-balance-history`) | **Date**: 2026-11-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/023-faster-balance-history/spec.md`

## Summary

A seed wallet watches about 80 addresses, and one Home refresh sends about 240 explorer requests:
- one address lookup per address for the balance;
- one transaction-list request per address for the history;
- one tip-height request per address inside the history, because that call skips the cache.

The plan cuts this in three layers:

1. **Shared read fixes**, which help every wallet type, multisig included:
   - one tip height per load;
   - concurrent identical reads share one request;
   - history is cached per address rather than per page size;
   - paging past the explorer's 25-transaction limit.
2. **Change-only sync**: the address lookup the wallet already makes (`GET /address/{a}`) gives confirmed and pending counts and amounts. Save it per address, compute the balance and pending amount from it, and fetch history pages only for addresses whose counts changed. The results are merged into a saved, explorer-shaped history. A background full re-read once per unlock catches what counts can't show, such as reorganisations.
3. **Display**:
   - Transactions, Receive and Send show the saved snapshot at once, as Home already does.
   - Home shows confirmed DGB as its main figure, with a separate pending line.
   - The gateway answers wallet-wide history in one pass.
   - `/dev/node` gets a change-only switch and a wallet-refresh benchmark with request counts.

The existing guarantees stay:
- A failed lookup fails the balance.
- Coins for signing are always read fresh.
- A send invalidates everything. This includes fixing today's `cache:balance` key mismatch, so the invalidation actually works.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA)

**Primary Dependencies**: Existing `HttpClient` + Polly pipeline, NBitcoin (gateway script and transaction parsing), `System.Text.Json`; no new packages

**Storage**: `ISecureStorage` (IndexedDB), the same store and protection as today's Home cache (clarification 2). New keys: `addr_summary_{walletId}_{network}`, `chain_history_{walletId}_{network}`, `dev:history-change-only`. Existing `tx_history_{walletId}` and `cache:*:{walletId}` are reused and now removed on wallet delete.

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` with a new counting explorer double, `SummaryExplorer` in `HistorySyncTestDoubles.cs`, reusing `TestMemoryStorage` as the fake `ISecureStorage`; the existing gateway test doubles for one-pass history. Manual checks in the browser preview and on `/dev/node`.

**Target Platform**: Installed PWA and mobile/desktop browsers

**Project Type**: Web app (Blazor WASM) with a separate wallet-logic library

**Performance Goals**:
- **SC-001**: a warm refresh with no activity sends N summary requests + ≤ 1 height request (N = watched addresses), down from about 3N.
- **SC-002**: change-only on is at least 50% faster than off on a warm refresh.
- **SC-005**: saved data is shown within 1 s.

**Constraints**:
- Fully client-side; the saved data never leaves the device.
- A partial failure never lowers a shown balance (FR-006).
- No change to the `IBlockchainService` signature; the new abilities are optional interfaces.
- Blazor WASM is single-threaded, so the background re-read gives way by waiting, not by threading.

**Scale/Scope**:
- About 80 addresses per seed wallet and up to a few thousand transactions; there's a 2,000-transaction safety cap per address.
- Saved history is about 1–2 KB per transaction.
- Code touched: about 5 wallet-layer services, 1 new sync service, 5 pages, 1 HTTP handler.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1. Still passes, and no amendment is needed.*

| Principle | Assessment |
|-----------|------------|
| I. Self-custody | No key or seed material is touched. Only public chain data (addresses, amounts, transactions) is read and saved. Pass. |
| II. In-browser cryptography | No signing changes. No server is added or changed; the only server read is the public explorer, already used. Pass. |
| III. Test-first (wallet logic) | All new logic (`WalletHistorySync`, summary source, cache entry, pager, gateway one-pass) is in `DigiByte.Wallet`, with tests written first in `DigiByte.Wallet.Tests`. Negative paths covered: failed lookup, partial page failure, incomplete reply, unknown saved version, endless paging, send during an in-flight read, cross-wallet isolation. Pass, enforced in tasks. |
| IV. Boundary discipline | Sync, caching, paging and summaries live in `DigiByte.Wallet`, the lowest layer that owns chain reads. `DigiByte.Web` only owns display snapshots, the dev switch's storage and request counting. No new reference direction. The fix to `BlockchainApiService` (tip-height reuse) also benefits `DigiByte.Pay.Api`, which uses it directly. Pass. |
| V. Resilience | No new backend dependency. Sources without summaries (node, gateway, regtest) fall back to today's path. A failed refresh keeps the saved figures with "Last updated…". The dev switch can turn change-only sync off without a release. Today's cached balance and history views are extended to more pages, not regressed. Pass. |
| Security & custody | No CSP change. No secrets logged. Request counts stay in memory. Saved data stays on the device and is deleted with the wallet (closes an existing gap: `tx_history_{id}` was left behind). Pass. |

## Project Structure

### Documentation (this feature)

```text
specs/023-faster-balance-history/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── history-sources.md
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Models/
│   ├── WalletInfo.cs                      # WalletBalance.UnconfirmedSatoshis now set; adds derived AvailableSatoshis/AvailableDgb
│   └── AddressSummary.cs                  # NEW: AddressSummary, AddressHistoryCursor, AddressHistoryPage
├── Services/
│   ├── IAddressSummarySource.cs           # NEW: optional summary + paging interface
│   ├── IWalletHistorySource.cs            # NEW: optional one-pass wallet history
│   ├── BlockchainApiService.cs            # summary + paging; 10 s tip-height memo; complete paging in GetAddressTransactionsAsync
│   ├── FallbackBlockchainService.cs       # in-flight sharing, generation-safe invalidation, txs:{address} key,
│   │                                      #   IAddressSummarySource routing, TryGetWalletHistorySource
│   ├── P2pGatewayBlockchainService.cs     # GetWalletTransactionsAsync (one pass)
│   ├── AddressSummaryStore.cs             # NEW: addr_summary_* and chain_history_* records (versioned)
│   ├── WalletHistorySync.cs               # NEW: refresh cycle, snapshot, merge, background full re-read
│   ├── HistorySyncOptions.cs              # NEW: ChangeOnly switch
│   └── WalletService.cs                   # balance/history/discovery use the sync; delete cleans up; send invalidates
src/DigiByte.Web/
├── Program.cs                             # register sync, options, counting handler on the explorer HttpClient
├── Services/
│   ├── WalletSnapshotCache.cs             # NEW: shared cache:*:{walletId} read/write/invalidate
│   └── RequestCountingHandler.cs          # NEW: in-memory request counts by kind (benchmark only)
├── Pages/
│   ├── Home.razor                         # main figure = ConfirmedDgb; pending line; uses WalletSnapshotCache
│   ├── Transactions.razor                 # saved snapshot first, background refresh, "Last updated"
│   ├── Receive.razor                      # same
│   ├── Send.razor                         # same for display; coins still fresh at confirm; fix cache:balance key
│   ├── PsbtSign.razor, Settings.razor     # fix cache:balance invalidation key
│   └── DevNode.razor                      # change-only toggle; wallet refresh benchmark with counts
tests/DigiByte.Wallet.Tests/
├── AddressSummaryTests.cs                 # NEW: derived values, DiffersFrom
├── AddressSummaryStoreTests.cs            # NEW: saved records, versions, isolation, delete cleanup
├── EsploraPagingTests.cs                  # NEW: summary parsing and strictness, paging, stop-at-known, cap, tip memo
├── FallbackCacheTests.cs                  # NEW: in-flight sharing, key per address, generation after send, summary routing
├── HistorySyncTests.cs                    # NEW: delta cycle, failures, pending, full re-read, switch, discovery, confirmations
├── HistorySyncTestDoubles.cs              # NEW: SummaryExplorer (counting, scripted) and WalletService builder
├── MultisigHistoryTests.cs                # NEW: multisig complete history over the real Esplora pager
├── GatewayWalletHistoryTests.cs           # NEW: one-pass equals per-address; parse count
└── (existing) ParallelQueryTests.cs, TransactionHistoryAddressTests.cs, GatewayWiringTests.cs  # updated where the key or counts change
docs/ARCHITECTURE.md                       # read path: summaries, change-only sync, saved history
```

**Structure Decision**: This uses the existing layered layout. The sync logic is a new service in `DigiByte.Wallet/Services`, next to `TransactionTracker` and the gateway sync services. The Web layer gets two small services and page changes only.

## Delivery order (for /speckit-tasks)

Each slice can ship on its own and keeps every test green:

1. **Shared read fixes** (US1 part, FR-001–003, FR-012/013 pager, multisig benefit): tip-height memo, cache entry with in-flight sharing and generation, `txs:{address}` key, complete paging.
2. **Send invalidation and cleanup** (FR-023, FR-025): fix the `cache:balance` key, delete the new and old saved keys with the wallet.
3. **Change-only sync** (US1 + US2, FR-004–011, FR-010a): summary source, store, `WalletHistorySync`, discovery seeding, background full re-read, dev switch.
4. **Pending balance** (US4, FR-014–016): `UnconfirmedSatoshis`, Home main figure and pending line, `TotalDgb` audit.
5. **Instant display** (US5, FR-017–019): `WalletSnapshotCache` on the four pages.
6. **Gateway one-pass** (US6, FR-020/021).
7. **Benchmark with counts** (FR-010b, SC-001/002) and docs.

## Complexity Tracking

No constitution violations to justify.
