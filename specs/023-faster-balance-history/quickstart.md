# Quickstart: Validating Faster Balance and History

**Feature**: [spec.md](spec.md) | **Contracts**: [contracts/history-sources.md](contracts/history-sources.md) | **Data model**: [data-model.md](data-model.md)

## Prerequisites

- .NET 10 SDK; the repo builds with `dotnet build`.
- For the live checks: the web app running locally (the dev server from `.claude/launch.json`), a mainnet or testnet seed wallet with some history, and network access to digiexplorer.info.

## 1. Automated tests

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~HistorySync|FullyQualifiedName~AddressSummary|FullyQualifiedName~FallbackCache|FullyQualifiedName~EsploraPaging|FullyQualifiedName~GatewayWalletHistory"
```

| Scenario | Expected | Spec |
|----------|----------|------|
| Warm refresh, no activity, counting explorer double, 80 addresses | 80 summary requests, 0 history pages, ≤ 1 tip height | US1, SC-001 |
| Balance and history called at the same time | Each address summary requested once | FR-004 |
| Two concurrent identical reads | One request to the double | FR-002 |
| History asked with take 50, 500, 1000 in a row | One history fetch per address | FR-003 |
| One address gets a new tx | Only that address paged; tx in result; balance includes it | US2, SC-003 |
| Pending tx confirms (total count unchanged) | Change detected; confirmations updated | US2 #2 |
| Pending tx dropped | Removed from saved history and pending sum | Edge case |
| One summary lookup fails | Balance read throws; saved summary for it unchanged | FR-006, FR-010, SC-006 |
| History page fails for one changed address | Balance returned; that address retried next cycle | FR-010 |
| Address with 60 confirmed txs | All 60 returned; next refresh with 1 new tx fetches one page | US3, SC-004, FR-013 |
| Faulty source that never ends paging | Stops at 2,000, marked truncated | R4 |
| Background full re-read | Starts after first result, waits during foreground refresh, failure changes nothing | FR-009 |
| Switch off | Every address's full history read; FR-001–004 still hold | FR-010a |
| Send then read | No value from before the send is served (including reads in flight at send time) | FR-023 |
| Saved record with unknown version | Discarded, full read | Edge case |
| Two wallets / two networks | Saved data never crosses | FR-025 |
| Delete wallet | Summary, chain history, tx_history and cache keys removed | FR-025 |
| Gateway one-pass vs per-address, 80 addresses × 200 txs | Same output; each tx parsed once | US6, FR-021, SC-007 |
| Pending incoming | `UnconfirmedSatoshis` > 0, `ConfirmedSatoshis` excludes it | US4, FR-014/015 |

The full suite still passes:

```powershell
dotnet test
```

## 2. Live benchmark (`/dev/node`)

1. Start the app, unlock a seed wallet, open `/dev/node`.
2. Check that **Change-only history sync** is on.
3. Run **Wallet refresh benchmark**.
4. Expected:
   - **On, warm**: summary requests = number of watched addresses, history pages = 0 (when nothing has changed), tip height ≤ 1.
   - **Off, warm**: history pages ≥ number of watched addresses, tip height ≤ 1.
   - On/warm time is at least 50% lower than off/warm (SC-002).
5. Turn the switch off, refresh Home, and confirm the balance and history are the same as with it on.

## 3. Live UI checks

| Check | Expected | Spec |
|-------|----------|------|
| Open Transactions, Receive and Send after one Home load, with the network throttled (DevTools "Slow 3G") | Saved balance/history visible within 1 s, then updated | US5, SC-005 |
| Go offline and open Transactions | Saved data plus "Last updated …"; no zero balance | FR-018, FR-024 |
| Receive a payment from another wallet | Home shows "Pending +X DGB" under an unchanged main figure; after confirmation it moves into the main figure | US4, FR-016 |
| Send a payment | Balance and history reflect it straight away | US2 #4, FR-023 |
| Switch wallets | Only the new wallet's saved figures show | US5 #3 |
| Multisig wallet with > 25 transactions | Full history shown | Clarification 4, FR-012 |
