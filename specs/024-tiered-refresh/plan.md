# Implementation Plan: Fewer Requests per Refresh and per Send

**Branch**: `feature/tiered-refresh` (spec directory `024-tiered-refresh`) | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/024-tiered-refresh/spec.md`

## Summary

After spec 023 a quiet refresh reads one address summary per watched address (~60), and a send reads coins for every address (~60). This plan:

1. **Tiered refresh**: a pure `HotAddressSelector` picks the addresses that can plausibly change; `WalletHistorySync` reads those every refresh (30 s reuse, as today) and reads the rest on a 5-minute cycle spread across refreshes. The balance is the sum of the summaries used, never lower.
2. **Full checks** stay where the user expects them: unlock, after a send, Home's refresh button and pull gesture, and Activity's pull to refresh (through `WalletService.RequestFullCheck()`).
3. **Coins only where there is money**: a pure `SendCoinPlanner` lets `SendAsync`, `SendBatchAsync` and `ConsolidateUtxosAsync` read coins only for funded addresses, falling back to every address before "Insufficient funds".
4. **Visibility**: "Fully checked N min ago" in Home's wallet info panel; a "Tiered refresh" switch and quiet-refresh/send counts on the `/dev/node` benchmark.

No server, storage format or signing change.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA)

**Primary Dependencies**: Existing only (`HttpClient` + Polly, NBitcoin); no new packages

**Storage**: `ISecureStorage`, unchanged records from spec 023. One new key: `dev:tiered-refresh` (per-device switch). Hot set and check times are in memory only.

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests`, reusing `SummaryExplorer` / `HistorySyncEnv` (spec 023) with the `FakeTimeProvider`; new pure-function tests for the selector and planner. Browser preview and `/dev/node` benchmark for Web.

**Target Platform**: Installed PWA and mobile/desktop browsers

**Project Type**: Web app (Blazor WASM) with a separate wallet-logic library

**Performance Goals**: SC-001 quiet refresh ≤ 12 requests (from ~60); SC-002 ≥ 60% fewer requests over 10 minutes at 15 s and 60 s poll paces; SC-004 a send from 2 funded addresses ≤ 4 coin reads (from ~60).

**Constraints**: A balance is never below the sum of the latest summaries; failed reads fail the refresh; coins are read fresh; a payment to a non-hot address shows within 5 minutes; tiering only with change-only sync on.

**Scale/Scope**: ~60 watched addresses per seed wallet; 2 new pure classes, changes to 2 wallet services, 3 pages, 1 dev page.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1. Still passes.*

| Principle | Assessment |
|-----------|------------|
| I. Self-custody | No key or seed material touched; only which public addresses are looked up and when. Pass. |
| II. In-browser cryptography | Signing unchanged; coins still read fresh in the browser before signing. Pass. |
| III. Test-first (wallet logic) | `HotAddressSelector`, the spreading rule, the reuse rule, `SendCoinPlanner` and the send fallback live in `DigiByte.Wallet` with tests written first, including negative paths: a failed hot read, a stale cold summary, a summary that under-reports funds at send time, a skipped address paid in between. Pass. |
| IV. Boundary discipline | Decisions in `DigiByte.Wallet`; Web only calls `RequestFullCheck`, shows the fully-checked time, and owns the switch's storage. No new reference direction. Pass. |
| V. Resilience | Fewer requests per refresh lowers rate-limit failures; every fallback (no summaries, switch off, under-reporting) returns to today's full behaviour. Pass. |
| Security & privacy | Fewer requests reveal fewer address-to-client links to the explorer; nothing new sent or stored. Pass. |

## Project Structure

### Documentation (this feature)

```text
specs/024-tiered-refresh/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── tiered-refresh.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Services/
│   ├── HotAddressSelector.cs      # NEW: pure hot-set rule (FR-002, FR-003, FR-016)
│   ├── SendCoinPlanner.cs         # NEW: funded vs rest split for sends (FR-010, FR-011)
│   ├── HistorySyncOptions.cs      # + Tiered switch (dev:tiered-refresh); FromStorage applies saved switches only with DevView on
│   ├── WalletHistorySync.cs       # tiered reuse rule, spread cold reads, GetFullyCheckedAt, AgeSnapshot
│   └── WalletService.cs           # hot hints, RequestFullCheck, coin reads via planner, PlanSendCoinReads
src/DigiByte.Web/
├── Program.cs                     # load dev:tiered-refresh at startup
├── Pages/
│   ├── Home.razor                 # ManualRefreshAsync → RequestFullCheck; "Fully checked" in the info panel
│   ├── Transactions.razor         # pull to refresh → RequestFullCheck
│   └── DevNode.razor              # Tiered refresh switch; quiet-refresh and send-count benchmark rows
tests/DigiByte.Wallet.Tests/
├── HotAddressSelectorTests.cs     # NEW
├── SendCoinPlannerTests.cs        # NEW
├── TieredRefreshTests.cs          # NEW: request counts, spreading, 5-min bound, full checks, balance floor
└── (existing) HistorySyncTests.cs, HistorySyncTestDoubles.cs  # doubles gain a coin-read counter
docs/ARCHITECTURE.md, docs/PROCESS_FLOWS.md  # read path: tiered refresh, send coin reads
```

**Structure Decision**: Extends the spec 023 layout; the two rules are pure classes next to `WalletHistorySync` so they can be tested without a chain double.

## Delivery order (for /speckit-tasks)

1. **Selector and reuse rule** (FR-001 to FR-005, FR-007, FR-016): `HotAddressSelector`, tiered reuse and spread in `WalletHistorySync`, hints from `WalletService`.
2. **Full checks** (FR-006): `RequestFullCheck`, Home and Activity wiring.
3. **Send coin reads** (FR-010 to FR-012, FR-015): `SendCoinPlanner` and the three send paths.
4. **Visibility and switch** (FR-008, FR-013, FR-014): fully-checked note, switch, benchmark rows.
5. **Docs and live measurement** (SC-001, SC-002, SC-004).

## Complexity Tracking

No constitution violations to justify.
