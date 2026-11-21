# Data Model: Fewer Requests per Refresh and per Send

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

Everything here is in memory, per unlocked session, in `DigiByte.Wallet` unless marked **Web**. Nothing new is saved except the developer switch.

## HotAddressInputs (new, value passed to the selector)

| Field | Type | Source |
|-------|------|--------|
| `Watched` | address list | `WalletService.GetAllAddresses()` |
| `Summaries` | address → `AddressSummary` | session snapshot (spec 023) |
| `OnShow` | address list | receive address at `NextReceivingIndex`: SegWit, plus the legacy address of the same index when it is watched |
| `HandedOut` | address list, newest 3 indexes | receive addresses below `NextReceivingIndex` with no activity: SegWit, plus the legacy address of each index when it is watched |
| `NextChange` | address? | change address at `NextChangeIndex` (SegWit) |
| `PendingSendInputs` | address set | wallet addresses spent by pending transactions in saved history |
| `RecentActivity` | address set | addresses in saved transactions with timestamp ≥ now − 7 days |

## HotSet (new, selector output)

| Field | Type | Rule |
|-------|------|------|
| `Hot` | address set | union of: summary with `Confirmed > 0` or any pending count/amount; `OnShow`; `HandedOut`; `NextChange`; `PendingSendInputs`; `RecentActivity` |
| `Cold` | address set | `Watched` minus `Hot` |
| `Tiered` | bool | false when `Watched.Count < 10` (FR-016): everything is treated as hot |

An address joins `Hot` as soon as any input shows activity on it (FR-003); nothing is remembered between refreshes beyond the snapshot and saved history, so the set is recomputed each time.

## Snapshot entry (existing, rule changed)

`(AddressSummary Summary, DateTimeOffset At)` per address, from spec 023.

| Address kind | Reused in an automatic refresh when |
|--------------|-------------------------------------|
| Hot | `now − At < 30 s` (unchanged) |
| Cold | `now − At < 5 min` and not chosen by the spread rule |
| Any, tiering off or change-only off | `now − At < 30 s` (as in 0.7.0) |

## RefreshCursor (new, per wallet and network)

| Field | Type | Meaning |
|-------|------|---------|
| `LastRefreshAt` | DateTimeOffset? | when the previous refresh of this wallet ran; drives the spread share |

Spread share per automatic refresh: `ceil(ColdCount × min(elapsed, 5 min) / 5 min)` oldest cold addresses, plus any cold address with `now − At ≥ 5 min`.

## FullyChecked (derived)

`min(At)` over the watched addresses' snapshot entries, or null if any address has none (always null while change-only sync is off, so the note is hidden). Shown in **Web** as "Fully checked N min ago" in Home's wallet info panel.

## SendCoinPlan (new, planner output)

| Field | Type | Rule |
|-------|------|------|
| `Funded` | address list | addresses whose current summary shows `Confirmed > 0` or any pending activity |
| `Rest` | address list | the other addresses in the key map |
| `Planned` | bool | false when any address lacks a summary read live this session within 5 minutes, tiering is off, or change-only is off: then `Funded` = every address and `Rest` is empty |

Fallback rule: when the coins read from `Funded` total less than amount + 0.01 DGB fee allowance, the coins of `Rest` are read before the send continues.

## HistorySyncOptions (changed)

| Field | Default | Source |
|-------|---------|--------|
| `ChangeOnly` | true | `dev:history-change-only` (spec 023) |
| `Tiered` | true | **Web** loads and saves `dev:tiered-refresh`; applies only when `ChangeOnly` is also true |
