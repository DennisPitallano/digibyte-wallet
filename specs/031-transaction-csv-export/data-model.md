# Data Model: Export Transaction History to CSV

**Feature**: [spec.md](spec.md) | **Research**: [research.md](research.md)

Nothing here is stored. Every entity lives only while an export runs, on the person's device.

## ExportRequest

What the person chose in the export sheet.

| Field | Type | Rules |
|---|---|---|
| `Asset` | `DGB` \| `DD` | Set by the screen the sheet was opened from (FR-014) |
| `Scope` | `Everything` \| `CurrentView` \| `DateRange` | Default `Everything` (FR-002) |
| `Preset` | `ThisMonth` \| `LastMonth` \| `ThisYear` \| `Custom` | Only when `Scope = DateRange` |
| `From`, `To` | local date | `Custom` only; `From ≤ To`; `To` not after today; both days inclusive (US2 scenario 2) |
| `CurrentView` | filter (`all`/`received`/`sent`), search text, selected wallet address | Copied from Activity when `Scope = CurrentView`. DD has no such view, so the option is hidden there |
| `FiatCurrency` | ISO code | From the app's fiat setting |

**Resolving the range.** The presets are turned into `[From, To]` local dates with an injected clock, for example "Last month" on 2026-03-15 gives 2026-02-01 to 2026-02-28. A row matches when its local date falls in `[From, To]`.

## ExportRow

One transaction, built from a `TransactionRecord` (DGB) or a `DigiDollarTransaction` (DD). The mapping is in research R6.

| Column (en header) | Field | Format |
|---|---|---|
| Date | `LocalTime` | `yyyy-MM-dd HH:mm:ss`, device local time |
| Time zone | `UtcOffset` | `UTC+hh:mm` / `UTC-hh:mm` for that row's instant (DST-aware) |
| Asset | `Asset` | `DGB` or `DD` |
| Type | `Kind` | Received · Sent · Own transfer · DigiDollar transfer/minted/redeemed (DGB file only) |
| Amount | `Amount` (decimal) | Signed (FR-005): + received, − sent, 0 own transfer or failed. DGB 8 decimals, DD 2 |
| Fee (DGB) | `FeeDgb` (decimal?) | Negative, sent and own-transfer rows only; empty otherwise |
| Price ({CUR}) | `Price` (decimal?) | Daily price per 1 unit of the asset; empty when not covered |
| Value ({CUR}) | `Value` (decimal?) | `Amount × Price`, rounded to the currency's decimals; empty when `Price` is empty |
| Status | `Status` | Confirmed · Unconfirmed · Failed (DD only) |
| Confirmations | `Confirmations` (int) | |
| Name | `Name` | Contact or own-wallet name; empty when none (FR-015) |
| Counterparty address | `CounterpartyAddress` | Empty when unknown |
| Wallet address | `WalletAddress` | First wallet address involved, with its label in the Name rules |
| Note | `Note` | OP_RETURN memo; empty when none |
| Transaction ID | `TxId` | |

The price headers carry the basis, for example `Price (USD, daily)`, or `Price (PHP, daily, via USD)` for currencies converted at each day's USD rate (research R3).

**Validation.** Text columns pass through formula neutralising and quoting (research R4). Numeric columns are formatted by the exporter only. Every row has a `TxId`. Within a file, `TxId` is unique.

## DailyPriceSeries

| Field | Type | Rules |
|---|---|---|
| `Currency` | ISO code | |
| `Basis` (type `PriceBasis`) | `Native` \| `ConvertedFromUsd` \| `Peg` | From the server's `basis`. `ConvertedFromUsd`: the server applied each day's USD rate. `Peg` for DD (1 DD = 1 USD, converted at each day's rate outside USD) |
| `Prices` | map local date → decimal | One entry per day; missing days are absent, never zero |
| `Requested` | `[From−1, To+1]` | The only dates sent outside the device |

`TryGet(date)` returns the price or nothing. A row with nothing gets empty Price and Value cells and is counted in `ExportResult.RowsWithoutPrice`.

## ExportResult

| Field | Meaning |
|---|---|
| `Outcome` | `Saved` · `Shared` · `Cancelled` · `NothingToExport` · `HistoryUnavailable` · `Locked` |
| `RowCount` | Rows written |
| `RowsWithoutPrice` | Rows with an empty Value (FR-007, told to the person) |
| `FileName` | `digibyte-{wallet-slug}-{DGB|DD}-{yyyy-MM-dd}.csv` (FR-011) |

## State flow of one export

```text
Idle ──Export──▶ Reading history ──(>1 s)──▶ shows progress + Cancel
   ▲                 │ fails / partial ──▶ HistoryUnavailable (Try again, no file)  [FR-006]
   │                 │ locked          ──▶ Locked (unlock first, no file)          [FR-019]
   │                 ▼
   │           Filtering by request ──▶ 0 rows ──▶ NothingToExport (no file)      [FR-017]
   │                 ▼
   │           Fetching daily prices (failure ⇒ prices empty, continue)          [FR-007]
   │                 ▼
   │           Writing CSV ──▶ Saving ──▶ Saved / Shared / Cancelled
   └──────────────── Cancel at any step before saving (no file)
```
