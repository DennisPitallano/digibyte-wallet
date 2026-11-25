# Implementation Plan: Export Transaction History to CSV

**Branch**: `feature/transaction-csv-export` (spec directory `031-transaction-csv-export`) | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/031-transaction-csv-export/spec.md` (GitHub issue #90)

## Summary

Add an **Export CSV** action to Activity (DGB) and to the DigiDollar screen (DD). It reads the wallet's *complete* history through a new strict read, writes one row per transaction with signed amounts, a separate DGB fee, local time plus a time-zone column, and a fiat value at each local day's price, and saves the file on the device. Prices come from one new date-range endpoint on the existing price proxy. That endpoint serves older days from daily price and exchange-rate archives shipped with the proxy and kept current by a monthly workflow, and recent days from the live sources. It also does any currency conversion, at each day's rate, so the wallet still makes one request. The file is built entirely in the browser. If the history can't be read completely, nothing is saved.

## Technical Context

**Language/Version**: C# / .NET 10 (Blazor WebAssembly for the wallet, ASP.NET Core minimal API for the price proxy), a few lines of JavaScript in `wwwroot/js/download.js`

**Primary Dependencies**: Existing only: `DigiByte.Wallet` services (`WalletService`, `DigiDollarService`, `FallbackBlockchainService`, `ContactService`), `IMemoryCache` and the `CoinGeckoProxy` HTTP client in `DigiByte.Api`. No new packages. New free upstream sources, called by the price proxy only (never by the wallet): Frankfurter (ECB rates) and the `exchange-api` daily snapshots, for daily USD exchange rates.

**Storage**: Nothing new on the wallet side; the export is built in memory and handed to the browser. The price proxy ships a read-only archive of daily DGB prices and daily USD exchange rates as data files (`src/DigiByte.Api/PriceArchive/*.csv`, ~170 KB per file), updated only by reviewed pull requests from a scheduled workflow.

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` (exporter, row mapping, ranges, prices, strict history read with test doubles) and `tests/DigiByte.Api.Tests` (range endpoint)

**Target Platform**: Browsers (desktop, installed PWA, mobile) for the wallet; the existing Linux container for `DigiByte.Api`

**Project Type**: Web app (static Blazor WASM client + small price proxy)

**Performance Goals**: 1,000 transactions exported in < 10 s on a laptop (SC-003); progress shown after 1 s

**Constraints**: No history data leaves the device (FR-016). No CSP change. Self-custody untouched. No partial file ever saved (FR-006).

**Scale/Scope**: Histories of several thousand transactions per wallet; 2 entry points, 1 shared sheet, 1 new API endpoint, ~60 new locale strings × 2 languages

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Result |
|---|---|---|
| I. Self-Custody | Export touches no keys, seed or password. The file holds public chain data and the person's own labels only. | ✅ Pass |
| II. In-Browser Cryptography / server roles | The new endpoint is a **price/data proxy**, an allowed role. It receives a currency, an asset and two dates, nothing about the wallet, and is not part of any signing flow. Its archives hold public market prices and exchange rates only, and its new upstream calls (Frankfurter, `exchange-api`) carry only currencies and dates. | ✅ Pass |
| III. Test-First for Wallet Logic | All new `DigiByte.Wallet` code (strict history read, DD full history, row mapping, CSV writer, ranges, price series) gets tests written first, including negative paths: failed page, partial history, missing prices, malicious labels, DST, comma locales. | ✅ Pass (gated in tasks) |
| IV. Boundary Discipline | Logic in `DigiByte.Wallet`; price fetching extends the existing price client in `DigiByte.Wallet` and the existing proxy in `DigiByte.Api`; UI in `DigiByte.Web`. No new references. | ✅ Pass |
| V. Resilience | A price outage leaves value cells empty and the export still succeeds. A history outage stops the export with Try again; it never affects balance, send, receive or the Activity list, which keep their own read paths. | ✅ Pass |
| Security: CSP | `api.coingecko.com` and the proxy host are already in `connect-src`. No change. | ✅ Pass |
| Security: secret hygiene | Proxy logs carry no query values. No new secrets. | ✅ Pass |
| Security: address handling | Export only displays addresses that came from the chain or saved contacts; no user-entered address is used. | ✅ N/A |
| Workflow: docs | `docs/ROADMAP.md` line "Transaction history CSV export" ticked; Roadmap page updated; `docs/PROCESS_FLOWS.md` gets the export flow; `docs/ARCHITECTURE.md` gets the price service's range endpoint, archives, refresh workflow and new outside sources. | ✅ Planned |

**Post-design re-check (after Phase 1)**: still all pass. Design decisions that touched a principle:
- R1 keeps the Activity read untouched (Principle V).
- R3 widens the price request by one day each side; it still carries no wallet data (Principle II, FR-016).
- R7 places all logic in existing projects (Principle IV).

## Project Structure

### Documentation (this feature)

```text
specs/031-transaction-csv-export/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0: R1–R8 decisions
├── data-model.md        # Phase 1: request, row, price series, result, state flow
├── quickstart.md        # Phase 1: tests + 20 manual scenarios
├── contracts/
│   ├── csv-file-format.md
│   ├── price-range-api.md
│   └── export-sheet-ui.md
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Services/
│   ├── WalletService.cs                 # + LoadCompleteHistoryAsync; record building pulled into one shared method
│   ├── IBlockchainService.cs            # + GetCompleteAddressHistoryAsync, GetDailyPricesAsync
│   ├── BlockchainApiService.cs          # Esplora: page to the end, throw on failure; daily prices
│   ├── BlockbookApiService.cs           # Blockbook: page=N until totalPages, throw on failure
│   ├── FallbackBlockchainService.cs     # route both new calls; no caching for complete reads; skip backends that don't support the complete read
│   ├── MockBlockchainService.cs         # implement the new interface members (NodeApiBlockchainService keeps the "not supported" default)
│   ├── IDigiDollarService.cs            # + LoadCompleteHistoryAsync
│   ├── DigiDollarService.cs             # + LoadCompleteHistoryAsync (follow `next`)
│   ├── ActivityFilter.cs                # NEW: Activity's filter/search rules, moved from Transactions.razor
│   └── Export/                          # NEW
│       ├── ExportRequest.cs             # scope, preset, range resolution (injected clock); ExportResult
│       ├── ExportRow.cs                 # one row of the file
│       ├── ExportRowMapper.cs           # TransactionRecord / DigiDollarTransaction → row
│       ├── DailyPriceSeries.cs          # lookup by local date; PriceBasis
│       ├── DailyPriceReducer.cs         # price points → one price per UTC day (shared with DigiByte.Api)
│       ├── CsvFormat.cs                 # separator/decimal choice, quoting, formula neutralising
│       ├── TransactionCsvWriter.cs      # header + rows → string (with BOM)
│       └── TransactionExportService.cs  # orchestrates: read → filter → prices → write → ExportResult
└── Models/FiatCurrencies.cs             # (read) decimals and native list

src/src.sln                                    # + tools/PriceArchiveImport project

src/DigiByte.Api/
├── DigiByte.Api.csproj                        # + PriceArchive\*.csv copied to output
├── Endpoints/PriceEndpoints.cs                # + GET /api/price/range (archive first, live for newer days; converts at each day's rate; asset=dgb|dd)
├── Services/PriceArchive.cs                   # NEW: loads PriceArchive/*.csv once, range lookup, 7-day rate carry-forward
└── PriceArchive/
    ├── dgb-daily-{currency}.csv               # NEW: DGB price per day, native currencies
    ├── usd-fx-{currency}.csv                  # NEW: units per 1 USD per day, converted currencies (+ native, for DD)
    └── README.md                              # source, seed date and first date of each file

tools/PriceArchiveImport/                      # NEW: console tool, history exports → archive format; --append-from-api for the workflow
.github/workflows/price-archive-refresh.yml    # NEW: monthly append + pull request

src/DigiByte.Web/
├── Components/Export/ExportCsvSheet.razor     # NEW shared sheet
├── Pages/Transactions.razor                   # + Export CSV button, passes current view
├── Pages/DigiDollar.razor                     # + Export CSV button
├── Pages/Help.razor                           # + mention of CSV export
├── Program.cs                                 # register TransactionExportService
├── wwwroot/js/download.js                     # + saveCsvFile (download or share) + getDecimalMark
└── wwwroot/locales/en.json, es.json           # + export.* strings

tests/DigiByte.Wallet.Tests/
├── Export/CsvFormatTests.cs
├── Export/TransactionCsvWriterTests.cs
├── Export/ExportRowMapperTests.cs
├── Export/ExportRequestTests.cs
├── Export/DailyPriceSeriesTests.cs
├── Export/TransactionExportServiceTests.cs
├── CompleteHistoryTests.cs                    # WalletService + readers: paging, caps, failures, unsupported backends
├── ActivityFilterTests.cs                     # Activity's filter/search rules, before they move to DigiByte.Wallet
└── DigiDollarCompleteHistoryTests.cs

tests/DigiByte.Api.Tests/
├── DigiByte.Api.Tests.csproj                  # + ProjectReference to tools/PriceArchiveImport
├── PriceRangeEndpointTests.cs
├── PriceArchiveTests.cs                       # archive loading, range lookup, 7-day rate carry-forward
└── PriceArchiveImportTests.cs                 # import tool normaliser (references tools/PriceArchiveImport)

docs/ROADMAP.md, docs/PROCESS_FLOWS.md, docs/ARCHITECTURE.md, src/DigiByte.Web/Pages/Roadmap.razor
```

**Structure Decision**: Existing monorepo layout. Pure logic goes in a new `Services/Export/` folder of `DigiByte.Wallet`, so it is unit-testable without a browser. The Web layer only gathers choices, calls `TransactionExportService`, and hands the string to `saveCsvFile`.

## Delivery order (maps to spec priorities)

1. **P1, US1**: strict DGB complete history (R1), row mapper, CSV writer, sheet with "Everything", Activity button, save. Shippable alone, with fiat columns present but empty.
2. **P2, US2**: current view and date ranges.
3. **P2, US3**: price and exchange-rate archives (seed + refresh workflow), price range endpoint and client, Price/Value columns.
4. **P2, US4**: DD complete history (R2), DigiDollar button.
5. **P3, US5**: names and wallet-address labels (mapper already has the fields; this wires `ContactService` and address labels).
6. Docs and roadmap.

## Risks

| Risk | Mitigation |
|---|---|
| CoinGecko free history covers only 365 days | Daily price archive in the proxy (research R3), seeded once and refreshed monthly by pull request; the workflow fails if the archive falls more than 300 days behind |
| Archive refresh pull requests not merged | The 365-day live window still covers recent days, so nothing breaks until ~11 months of neglect; the failing workflow flags it first |
| Free daily exchange-rate history is short for some currencies (ECB-published ones go back to 1999, others start in 2024) | Rows before a currency's first rate have no value and are counted; each currency's source and first date are recorded in `PriceArchive/README.md` |
| Very large wallets read slowly from explorers | Progress + Cancel; reads reuse the existing 10-way concurrency |
| Extracting the record-building code from `LoadTransactionHistoryAsync` could change Activity | Existing Activity/history tests run unchanged; the extraction is a pure move done first, with no behaviour change |

## Complexity Tracking

No constitution violations. Nothing to justify.
