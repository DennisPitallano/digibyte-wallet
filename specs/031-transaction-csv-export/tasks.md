---

description: "Task list for Export Transaction History to CSV (spec 031, issue #90)"
---

# Tasks: Export Transaction History to CSV

**Input**: Design documents from `specs/031-transaction-csv-export/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included and written first. Constitution Principle III makes tests mandatory for every change to `DigiByte.Wallet`, and they must fail before the code they cover is written. Web-layer (Razor/JS) changes are checked through [quickstart.md](quickstart.md).

**Organization**: Tasks are grouped by user story, so each story can be built and checked on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1–US5 from spec.md)

## Path Conventions

Existing monorepo: `src/DigiByte.Wallet/` (logic), `src/DigiByte.Api/` (price proxy), `src/DigiByte.Web/` (UI), `tests/DigiByte.Wallet.Tests/`, `tests/DigiByte.Api.Tests/`.

---

## Phase 1: Setup

**Purpose**: Folders, the branch, and a safe refactor that later phases build on.

- [X] T001 Create the branch `feature/transaction-csv-export` from `main`, then create the folders `src/DigiByte.Wallet/Services/Export/`, `src/DigiByte.Web/Components/Export/` and `tests/DigiByte.Wallet.Tests/Export/`
- [X] T002 Run `dotnet test tests/DigiByte.Wallet.Tests` and `dotnet test tests/DigiByte.Api.Tests` on the untouched branch and note the baseline pass count in the PR description draft, so later regressions are visible

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The CSV format rules, the row model and the strict "complete history" read. Every story depends on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests first (must fail before T008–T014)

- [X] T003 [P] Write `tests/DigiByte.Wallet.Tests/Export/CsvFormatTests.cs` covering [contracts/csv-file-format.md](contracts/csv-file-format.md) "Cell rules":
  - dot locale → separator `,` and decimal `.`; comma locale → separator `;` and decimal `,`
  - a field containing the separator, `"`, CR or LF is wrapped in quotes with inner `"` doubled
  - a text field starting with `=`, `+`, `-`, `@`, TAB or CR gets a leading `'`, while a numeric `-1.5` is unchanged
  - an empty value is written as an empty field, never `0`, `-` or `N/A`
  - amounts have no thousands separator: DGB 8 decimals, DD 2 decimals, fiat with the currency's decimals from `FiatCurrencies`
- [X] T004 [P] Write `tests/DigiByte.Wallet.Tests/Export/TransactionCsvWriterTests.cs`:
  - output starts with BOM `EF BB BF`, uses CRLF line endings, and line 1 is the 15-column header in the exact order of the contract
  - rows are newest first with no duplicate Transaction ID
  - zero rows → the writer refuses (throws or returns null) and never produces a header-only file (FR-017)
  - English and Spanish headers match the contract table
- [X] T005 [P] Write `tests/DigiByte.Wallet.Tests/Export/ExportRowMapperTests.cs` for DGB `TransactionRecord` rows, using the research R6 table:
  - Received → `+Amount`, Fee empty
  - Sent → `−Amount` (fee excluded) and `−Fee`
  - Sent with `AmountSatoshis == 0` → Type "Own transfer", Amount 0, `−Fee`
  - `DigiDollar != null` → Type from `DigiDollarTxSummary.Label`
  - Confirmations 0 → Status "Unconfirmed"
  - Date is `yyyy-MM-dd HH:mm:ss` in an injected `TimeZoneInfo`, with an offset column `UTC±hh:mm`; two rows on either side of a DST change carry different offsets; a 23:30 local row keeps its local date even though it is the next day in UTC
- [X] T006 [P] Write `tests/DigiByte.Wallet.Tests/CompleteHistoryTests.cs` for the strict reads (research R1):
  - Esplora `GetCompleteAddressHistoryAsync` pages past 2,000 transactions until the last page
  - Blockbook pages `page=N&pageSize=1000` until `totalPages`
  - a failed page throws, never returns a partial or empty list
  - `WalletService.LoadCompleteHistoryAsync` throws `HistoryIncompleteException` when any address fails, including partway through
  - it never reads saved snapshots or the change-only cache
  - it reports progress as addresses done / total
  - it honours cancellation
  - it builds the same `TransactionRecord`s as `LoadTransactionHistoryAsync` for the same chain data
  - it throws when the wallet is locked (FR-019)
  - the fallback skips a backend that throws `NotSupportedException` and uses the next explorer; with only unsupported backends it throws `HistoryIncompleteException`
- [X] T007 Run `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Export|FullyQualifiedName~CompleteHistory"` and confirm the new tests fail (compile or assert) before implementing

### Implementation

- [X] T008 Move the `TransactionRecord`-building block (the `explorerRecords` projection, merge with local records and `CopyMissing`) out of `LoadTransactionHistoryAsync` into one private method in `src/DigiByte.Wallet/Services/WalletService.cs`, with no behaviour change. Run the existing wallet tests and confirm they all still pass (plan Risks).
- [X] T009 [P] Implement `src/DigiByte.Wallet/Services/Export/CsvFormat.cs`. It has two styles (`Comma` = `,` + `.`, `Semicolon` = `;` + `,`), chosen from a decimal-mark character, and `Text(string?)` (neutralise + quote), `Number(decimal?, int decimals)` and `Empty()` helpers, all per research R4.
- [X] T010 [P] Create `src/DigiByte.Wallet/Services/Export/ExportRow.cs` with the fields of data-model.md "ExportRow":
  - `LocalTime`, `UtcOffset`, `Asset` (`DGB` | `DD`), `Kind`, `Amount`, `FeeDgb` (decimal?), `Price` (decimal?), `Value` (decimal?), `Status` (Confirmed · Unconfirmed · Failed), `Confirmations`, `Name`, `CounterpartyAddress`, `WalletAddress`, `Note`, `TxId`
  - rule: "Every row has a `TxId`. Within a file, `TxId` is unique."
- [X] T011 Implement `ExportRowMapper.FromRecord(TransactionRecord, TimeZoneInfo, Func<string, string?> nameOf, Func<string, string?> walletLabelOf)` in `src/DigiByte.Wallet/Services/Export/ExportRowMapper.cs` per research R6 (DGB rows; DD rows come in US4).
- [X] T012 Implement `src/DigiByte.Wallet/Services/Export/TransactionCsvWriter.cs`. `Write(IReadOnlyList<ExportRow>, CsvFormat style, string language, string fiatCurrency, PriceBasis basis)` returns the full file text with the BOM, writes headers from the contract (en/es), and puts the currency and basis in the Price/Value headers. It must not be called with zero rows.
- [X] T013 Add `GetCompleteAddressHistoryAsync(string address, CancellationToken ct)` to `src/DigiByte.Wallet/Services/IBlockchainService.cs`, as a default interface member that throws `NotSupportedException`, so the 30 existing implementers and test doubles keep compiling. Implement it for real in:
  - `BlockchainApiService.cs` (Esplora `after_txid` paging, no `MaxTransactionsPerAddress` cap, throw on failure)
  - `BlockbookApiService.cs` (`page=N&pageSize=1000` until `totalPages`, throw on failure)
  - `FallbackBlockchainService.cs` (try explorers in order, not cached, `NodeRouteClass.History`, never the node). A backend that throws `NotSupportedException` is **skipped**, not counted as a failed read; the call throws only when no explorer that supports the method could answer, or when every backend is unsupported.
  - `MockBlockchainService.cs` (return the mock list)
  - `NodeApiBlockchainService` and the SPV gateway source keep the default (not supported): the node cannot list full history (see the `FallbackBlockchainService` history comment)
- [X] T014 Add `HistoryIncompleteException`, `HistoryExportProgress(int Done, int Total)` and `WalletService.LoadCompleteHistoryAsync(IProgress<HistoryExportProgress>?, CancellationToken)` in `src/DigiByte.Wallet/Services/WalletService.cs`:
  - call `EnsureUnlocked()` first
  - use the gateway `IWalletHistorySource` when it is active, otherwise `GetCompleteAddressHistoryAsync` per address with the existing 10-way semaphore
  - build records with the method extracted in T008 and merge local records
  - throw `HistoryIncompleteException` on any failure
- [X] T015 Run `dotnet test tests/DigiByte.Wallet.Tests` until T003–T006 pass along with every existing test

**Checkpoint**: The wallet can produce a correct, complete DGB CSV string in tests. No UI yet.

---

## Phase 3: User Story 1 – Download my whole DGB history as a spreadsheet file (Priority: P1) 🎯 MVP

**Goal**: Activity → Export CSV → Everything → Export saves the complete DGB history as a CSV that opens correctly in Excel, Sheets and LibreOffice.

**Independent Test**: quickstart scenarios 1–5, 1b (more than 1,000 transactions), 6 (formula), 11 (offline), 15 (comma region), 16 (phone), 18 (locked), 20 (large wallet).

### Tests for User Story 1

- [X] T016 [P] [US1] Write `tests/DigiByte.Wallet.Tests/Export/TransactionExportServiceTests.cs`:
  - `Everything` exports every record from a fake `LoadCompleteHistoryAsync`, newest first
  - `HistoryIncompleteException` → `ExportResult.Outcome = HistoryUnavailable` and no content
  - locked → `Locked`
  - zero transactions → `NothingToExport` and no content
  - cancellation → `Cancelled`
  - the file name is `digibyte-{wallet-slug}-DGB-{yyyy-MM-dd}.csv`, where the slug is lower-case ASCII letters, digits and dashes, or `wallet` when empty (research R8)
  - Price and Value are empty when no price series is supplied

### Implementation for User Story 1

- [X] T017 [US1] Create `src/DigiByte.Wallet/Services/Export/ExportRequest.cs` with `Asset` and `Scope = Everything` (other scopes come in US2), and `ExportResult` with `Outcome` (`Saved` · `Shared` · `Cancelled` · `NothingToExport` · `HistoryUnavailable` · `Locked`), `RowCount`, `RowsWithoutPrice`, `FileName` and `Content`, per data-model.md.
- [X] T018 [US1] Implement `src/DigiByte.Wallet/Services/Export/TransactionExportService.cs`:
  - `ExportAsync(ExportRequest, ExportEnvironment env, IProgress<HistoryExportProgress>?, CancellationToken)`, where `env` carries the decimal mark, language, `TimeZoneInfo`, clock, wallet name and the name/label lookups
  - flow: read → map → zero check → write → `ExportResult`, following the data-model "State flow"
  - then make T016 pass
- [X] T019 [US1] Register `TransactionExportService` as scoped in `src/DigiByte.Web/Program.cs`, next to `ContactService`.
- [X] T020 [P] [US1] Add `saveCsvFile(content, filename)` and `getDecimalMark()` to `src/DigiByte.Web/wwwroot/js/download.js`:
  - `saveCsvFile` returns `saved`, `shared` or `cancelled`
  - it uses `navigator.share` with a `text/csv` `File` when `matchMedia('(pointer: coarse)').matches && navigator.canShare?.({files:[file]})`, otherwise an anchor download with `text/csv;charset=utf-8`, revoking the object URL after 1 s
  - `getDecimalMark` returns the decimal mark from `Intl.NumberFormat().formatToParts(1.1)`
- [X] T021 [P] [US1] Add the `export.*` strings for US1 to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`:
  - the button, the sheet title (DGB/DD) and the "Everything" option
  - the FR-012 notice, plus its hidden-amounts addition
  - the Export and Cancel buttons
  - the progress text "Reading history… {0} of {1} addresses"
  - the messages for saved, history unavailable + Try again, nothing to export, and locked
  - the CSV header names and Type/Status words from [contracts/csv-file-format.md](contracts/csv-file-format.md)
- [X] T022 [US1] Build `src/DigiByte.Web/Components/Export/ExportCsvSheet.razor` per [contracts/export-sheet-ui.md](contracts/export-sheet-ui.md):
  - dialog with focus trap and focus returned on close
  - "What to export" radios (Everything only for now) and the always-shown FR-012 notice
  - Export runs `TransactionExportService` and passes it the decimal mark from `getDecimalMark`
  - the progress bar with Cancel appears only after 1 s (research R8), with `aria-live="polite"`
  - inline states for HistoryUnavailable (with Try again), NothingToExport and Locked
  - on success it calls `saveCsvFile` and shows a toast
- [X] T023 [US1] Add the **Export CSV** button to the header row of `src/DigiByte.Web/Pages/Transactions.razor`, next to the filters. It opens `ExportCsvSheet` with `Asset = DGB` and passes `hideAmounts` for the notice. It is disabled with "Unlock your wallet to export" while the wallet is locked (FR-019).
- [ ] T024 [US1] Run quickstart scenarios 1, 1b, 2–6, 11, 15, 16, 18 and 20 in the browser preview and record the results in the PR description. Scenario 1 confirms no more than 3 actions to a saved file (SC-001); scenario 1b compares an "Everything" export of a wallet with more than 1,000 transactions against an explorer's count (SC-002).

**Checkpoint**: MVP. A shop owner can export the full DGB history. Price and Value columns exist but are empty.

---

## Phase 4: User Story 2 – Export only what I need (Priority: P2)

**Goal**: Choose Everything, What the list shows, or a date range (This month, Last month, This year, Custom).

**Independent Test**: quickstart scenarios 7, 8 and 12.

### Tests for User Story 2

- [ ] T025 [P] [US2] Write `tests/DigiByte.Wallet.Tests/Export/ExportRequestTests.cs`:
  - with the clock at 2026-03-15: "This month" = 2026-03-01..2026-03-15, "Last month" = 2026-02-01..2026-02-28, "This year" = 2026-01-01..2026-03-15
  - Custom: `From ≤ To` and `To` not after today, otherwise invalid
  - a row matches when its **local** date is in `[From, To]` inclusive (23:30 local on the last day is included)
  - `CurrentView` applies the filter (`all`/`received`/`sent`), the search text and the selected wallet address exactly as Activity's `ApplyFilter` does
- [ ] T026 [P] [US2] Extend `TransactionExportServiceTests.cs`: a range or view that matches nothing → `NothingToExport` with no content (US2 scenario 3)
- [ ] T027 [P] [US2] Write `tests/DigiByte.Wallet.Tests/ActivityFilterTests.cs` for `ActivityFilter.Matches` before T028 moves the code. Copy today's `ApplyFilter` rules from `src/DigiByte.Web/Pages/Transactions.razor` into cases:
  - filter `all`/`received`/`sent`
  - search, case-insensitive, as `Matches` in the page does today: the contact name (`NameFor`), `CounterpartyAddress`, the wallet addresses and their labels (`ActivityAddressIndex.MatchesText`), `Memo`, `TxId`, and `AmountDgb` written as `0.########` in invariant culture
  - the selected wallet address, matched with `ActivityAddressIndex.Matches`
  - an empty query matches everything
  Constitution Principle III: these tests must fail (the class doesn't exist yet) before T028.

### Implementation for User Story 2

- [ ] T028 [US2] Move Activity's filter and search matching (`ApplyFilter` in `src/DigiByte.Web/Pages/Transactions.razor`) into a static `ActivityFilter.Matches(TransactionRecord, string filter, string query, string? address, …)` in `src/DigiByte.Wallet/Services/ActivityFilter.cs`. Call it from the page, so the list and the export cannot disagree. Existing behaviour is unchanged. Make T027 pass.
- [ ] T029 [US2] Extend `ExportRequest.cs`:
  - `Scope` = `Everything` | `CurrentView` | `DateRange`; `Preset` = `ThisMonth` | `LastMonth` | `ThisYear` | `Custom`; `From`/`To` local dates
  - `Resolve(clock, tz)` turns a preset into a range
  - `Matches(row)` uses the range or `ActivityFilter`
  - make T025 pass
- [ ] T030 [US2] Apply `ExportRequest.Matches` in `TransactionExportService.ExportAsync`, after reading and before the zero check. Make T026 pass.
- [ ] T031 [P] [US2] Add the `export.*` strings to `en.json` and `es.json`: "What the list shows", "Date range", "This month", "Last month", "This year", "Custom", "From", "To", and an invalid-range message
- [ ] T032 [US2] Extend `ExportCsvSheet.razor`:
  - the "What the list shows" radio, only when `Asset = DGB`, using the current view passed in from `Transactions.razor`
  - the date-range radio with preset buttons and From/To date inputs, and Export disabled while the range is invalid
- [ ] T033 [US2] Pass `filter`, `query` and `selectedAddress` from `Transactions.razor` into the sheet. Run quickstart scenarios 7, 8 and 12.

**Checkpoint**: US1 and US2 both work. Month-end exports are possible.

---

## Phase 5: User Story 3 – Books in my own currency (Priority: P2)

**Goal**: Price and Value columns hold each local day's price in the person's currency, fetched once per export as a date-range series.

**Independent Test**: quickstart scenarios 9, 10 and 19.

### Tests for User Story 3

- [ ] T034 [P] [US3] Write `tests/DigiByte.Api.Tests/PriceRangeEndpointTests.cs` for [contracts/price-range-api.md](contracts/price-range-api.md):
  - `currency` outside the wallet's `FiatCurrencies` list → 400; `asset` other than `dgb`/`dd` → 400
  - `from > to` → 400; `to` after tomorrow (UTC) → 400
  - a converted currency returns `basis: convertedFromUsd` with `price = USD price × that day's rate`; a Saturday uses Friday's rate; a day more than 7 days after the last rate is absent
  - `asset=dd` returns `basis: peg` with each day's USD rate (1.00 for `usd`)
  - an upstream `market_chart/range` reply (stubbed handler on the `CoinGeckoProxy` client) is reduced to one entry per UTC day (the last point of the day), ascending, with no zero-filled days
  - `from` before 2014-01-01 → 400; a range of several years is accepted (no span limit)
  - archived days come from the archive and win over live values for the same day
  - a range that ends on or before the archive's last date makes **no** upstream call
  - upstream 429 or 5xx with archived days in range → 200 with the archived days; with none → the error is passed back
  - a second identical call is served from cache with no upstream call
- [ ] T035 [P] [US3] Write `tests/DigiByte.Wallet.Tests/Export/DailyPriceSeriesTests.cs`:
  - `TryGet(date)` by local date; a missing day returns nothing, never 0
  - `Basis` is taken from the response's `basis`; the client never converts anything itself
  - for DD in USD no request is made and every day is 1.00
  - the client asks for `[From−1, To+1]` with only `currency`, `asset`, `from` and `to`, in one request, also for converted currencies (FR-016)
  - an upstream failure gives an empty series with `Failed = true` and never throws
  - the direct-mode URL is the CoinGecko `coins/digibyte/market_chart/range` URL
- [ ] T036 [P] [US3] Extend `TransactionExportServiceTests.cs`:
  - two equal amounts on days with different prices get different Values (US3 scenario 2)
  - rows without a price have empty Price/Value and are counted in `RowsWithoutPrice`
  - a price failure still yields `Saved` content (FR-007)
  - the Price/Value headers carry the currency and basis

- [ ] T037 [P] [US3] Write `tests/DigiByte.Api.Tests/PriceArchiveTests.cs`:
  - loads `dgb-daily-{currency}.csv` lines `yyyy-MM-dd,price` and `usd-fx-{currency}.csv` lines `yyyy-MM-dd,rate` once per process
  - a rate lookup on a day with no rate returns the most recent earlier rate up to 7 days old, and nothing beyond that
  - a range lookup returns ascending days, inclusive, and absent days stay absent
  - an unknown currency returns no archive (the endpoint falls back to live)
  - a malformed line fails loading with the file name and line number, so a bad refresh PR breaks tests rather than production
  - `LastDate` is the file's last day
- [ ] T038 [P] [US3] Write `tests/DigiByte.Api.Tests/PriceArchiveImportTests.cs` for the `tools/PriceArchiveImport` normaliser:
  - a CoinGecko history export (header `snapped_at,price,market_cap,total_volume`, timestamps in UTC) becomes one `yyyy-MM-dd,price` line per UTC day, ascending, with no duplicates
  - rows with an empty or zero price are dropped
  - a Frankfurter time-series reply and an `exchange-api` daily snapshot each become `yyyy-MM-dd,rate` lines (units per 1 USD), ascending, with no duplicates

### Implementation for User Story 3

- [ ] T039 [US3] Create `src/DigiByte.Api/Services/PriceArchive.cs`, a singleton registered in `src/DigiByte.Api/Program.cs`, that reads `PriceArchive/dgb-daily-{currency}.csv` and `PriceArchive/usd-fx-{currency}.csv` from the content root on first use, with the 7-day rate carry-forward. Add `<Content Include="PriceArchive\*.csv" CopyToOutputDirectory="PreserveNewest" />` to `src/DigiByte.Api/DigiByte.Api.csproj`. Make T037 pass.
- [ ] T040 [US3] Create the console tool `tools/PriceArchiveImport/` (added to `src/src.sln`), which turns a CoinGecko DGB historical-data export into the archive format, and add a `ProjectReference` to it from `tests/DigiByte.Api.Tests/DigiByte.Api.Tests.csproj` so T038 can call its normaliser. Make T038 pass. Then, for each currency in `FiatCurrencies`' CoinGecko-native list, download CoinGecko's full DGB history export (a manual, one-time maintainer step), run the tool, and commit `src/DigiByte.Api/PriceArchive/dgb-daily-{currency}.csv`. For each converted currency in `FiatCurrencies` (and every native one, for DD), seed `usd-fx-{currency}.csv` from Frankfurter (ECB) when it publishes that currency, otherwise from the `exchange-api` daily snapshots. Record each file's source, seed date and first date in `src/DigiByte.Api/PriceArchive/README.md`.
- [ ] T041 [US3] Add `.github/workflows/price-archive-refresh.yml` per the "Archive refresh" section of [contracts/price-range-api.md](contracts/price-range-api.md):
  - schedule monthly plus `workflow_dispatch`
  - append days after each file's last date, excluding today, for both the price files and the rate files (each rate file from its recorded source)
  - open one pull request with the default `GITHUB_TOKEN` and `contents: write` + `pull-requests: write` only; never auto-merge
  - fail when any file is more than 300 days behind
  - it reuses `tools/PriceArchiveImport` in a `--append-from-api` mode
- [ ] T042 [US3] Add `GET /range` to `src/DigiByte.Api/Endpoints/PriceEndpoints.cs`:
  - validates as in the contract
  - takes archived days from `PriceArchive` (T039); only for days after the archive's last date does it make one upstream call through `CoinGeckoProxy`; archive values win; an upstream failure still returns the archived days
  - for converted currencies, multiplies the USD price by that day's rate (archive, then one live Frankfurter or `exchange-api` call for newer days) and returns `basis: convertedFromUsd`; for `asset=dd`, returns each day's USD rate with `basis: peg`
  - the cache key adds the asset: `price:range:{asset}:{currency}:{from}:{to}`
  - reduces the points to daily with a shared helper `DailyPriceReducer` in `src/DigiByte.Wallet/Services/Export/DailyPriceReducer.cs` (`DigiByte.Api` already references `DigiByte.Wallet`; the wallet's direct mode uses the same helper)
  - caches for 1 hour and logs only status codes
  - make T034 pass
- [ ] T043 [US3] Create `src/DigiByte.Wallet/Services/Export/DailyPriceSeries.cs`, with `Currency`, `Basis` (`Native` | `ConvertedFromUsd` | `Peg`), `Prices` (map local date → decimal; "Missing days are absent, never zero"), `Requested` and `Failed`, plus `TryGet`.
- [ ] T044 [US3] Add `GetDailyPricesAsync(string fiatCurrency, string asset, DateOnly from, DateOnly to, CancellationToken ct)` to `IBlockchainService` (default member returning an empty, failed series) and implement it in `BlockchainApiService.cs` and `FallbackBlockchainService.cs`:
  - proxy URL `{_priceApiBaseUrl}/range?currency=&asset=&from=&to=`, or in direct mode the CoinGecko URL for native currencies only (converted currencies get an empty series there)
  - no exchange-rate lookup in the wallet: `Basis` comes from the response
  - never throws
  - make T035 pass
- [ ] T045 [US3] In `TransactionExportService.ExportAsync`, after filtering:
  - request the series once for `[min local date − 1, max local date + 1]`, set `Price` and `Value = round(Amount × Price, currency decimals)` per row, and count `RowsWithoutPrice`
  - pass the currency and basis to the writer
  - make T036 pass
- [ ] T046 [P] [US3] Add strings to `en.json` and `es.json`: "Getting daily prices…", "{0} transactions exported. {1} have no {2} value (no price was available for those days).", and the header suffix ", via USD" / ", vía USD"
- [ ] T047 [US3] Show the "Getting daily prices…" state and the price-gap toast in `ExportCsvSheet.razor`. Run quickstart scenarios 9, 9b, 10 and 19 (DevTools Network: only one `/api/price/range` request leaves the device, also for a converted currency).

**Checkpoint**: Exports are bookkeeping-ready, with each local day's fiat value.

---

## Phase 6: User Story 4 – Export DigiDollar (DD) history too (Priority: P2)

**Goal**: The DigiDollar screen exports the complete DD history with the same 15 columns; amounts are in DD and the fee is in DGB.

**Independent Test**: quickstart scenarios 13 and 14.

### Tests for User Story 4

- [ ] T048 [P] [US4] Write `tests/DigiByte.Wallet.Tests/DigiDollarCompleteHistoryTests.cs` using the doubles in `DdTestDoubles.cs`:
  - `LoadCompleteHistoryAsync` follows `next` until null, with page size 200, for every address chunk
  - a failed page throws `HistoryIncompleteException`
  - pending sends are merged through `BuildHistory`
  - the cached last-50 history is never used
- [ ] T049 [P] [US4] Extend `ExportRowMapperTests.cs` with `FromDd(DigiDollarTransaction, …)` per the research R6 table:
  - Received → `+AmountCents/100`
  - Sent → `−AmountCents/100` and Fee `−FeeSats` in DGB
  - SelfTransfer → "Own transfer", 0, Fee
  - `DdTxStatus.Failed` → Status "Failed", Amount 0
  - Asset `DD`, 2 decimals
- [ ] T050 [P] [US4] Extend `TransactionCsvWriterTests.cs`: for the same language and currency, the DD and DGB header lines are byte-identical: the same 15 columns in the same order (US4 scenario 3)

### Implementation for User Story 4

- [ ] T051 [US4] Add `LoadCompleteHistoryAsync(IProgress<HistoryExportProgress>?, CancellationToken)` to `src/DigiByte.Wallet/Services/IDigiDollarService.cs` and implement it in `DigiDollarService.cs` per research R2, reusing `BuildHistory`/`Classify`. Make T048 pass.
- [ ] T052 [US4] Implement `ExportRowMapper.FromDd` in `ExportRowMapper.cs`. Make T049 pass.
- [ ] T053 [US4] Branch `TransactionExportService.ExportAsync` on `Asset`. DD reads `IDigiDollarService.LoadCompleteHistoryAsync`, maps with `FromDd`, and uses a `Peg` price series: 1.00 per day for USD with no request, otherwise one `/api/price/range?asset=dd` request that returns each day's USD rate. The file name uses `DD`. Make T050 pass.
- [ ] T054 [US4] Add the **Export CSV** button above the history list in `src/DigiByte.Web/Pages/DigiDollar.razor`, opening `ExportCsvSheet` with `Asset = DD` (no "What the list shows" option). Run quickstart scenarios 13 and 14.

**Checkpoint**: DGB and DD files can be pasted together.

---

## Phase 7: User Story 5 – Notes and counterparties for bookkeeping (Priority: P3)

**Goal**: Rows carry the contact or wallet name, the counterparty address, the wallet address with its label, and the on-chain note.

**Independent Test**: name a contact, export, and check that row (US5 scenarios 1–2).

### Tests for User Story 5

- [ ] T055 [P] [US5] Extend `ExportRowMapperTests.cs`:
  - the counterparty matching a contact (case-insensitive, normalized address) → Name = contact name
  - an own-wallet contact → the wallet name
  - no match → empty Name, never a placeholder
  - Wallet address = the first of `WalletAddresses`
  - Note = `Memo`
  - a contact named `@SUM(1)` is written as `'@SUM(1)` (SC-006)

### Implementation for User Story 5

- [ ] T056 [US5] Build the name lookup from `ContactService.GetAllWithWalletsAsync(WalletService)` and the wallet-address label lookup from the same source `Transactions.razor` uses (`LoadWalletLabelsAsync`). Pass both through `ExportEnvironment` from `ExportCsvSheet.razor`, for both DGB and DD. Make T055 pass and run US5's independent test.

**Checkpoint**: All five stories work on their own.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T057 [P] Tick "Transaction history CSV export" in `docs/ROADMAP.md` and the matching line in `src/DigiByte.Web/Pages/Roadmap.razor`
- [ ] T058 [P] Add an "Export transaction history" flow to `docs/PROCESS_FLOWS.md`: read complete history → filter → daily prices → write → save, with the failure branches from data-model.md. In `docs/ARCHITECTURE.md`, add to the price service's section:
  - the `/api/price/range` endpoint and its `asset` parameter
  - the `PriceArchive/` price and exchange-rate files
  - the monthly refresh workflow
  - the new outside sources (Frankfurter, `exchange-api`), called by the price service only
- [ ] T059 [P] Mention CSV export in the user Help page `src/DigiByte.Web/Pages/Help.razor` (en/es strings in the locale files)
- [ ] T060 Check every new `export.*` key exists in both `en.json` and `es.json` with no English left in `es.json` (FR-018)
- [ ] T061 Performance check: export 1,000 transactions on a laptop and confirm < 10 s, with progress appearing after 1 s (SC-003); if not, profile the read before touching the writer
- [ ] T062 Open the same DGB file in Excel, Google Sheets and LibreOffice in a dot region and a comma region (SC-004), and record screenshots in the PR
- [ ] T063 Run every scenario in quickstart.md (including 1b and 9b) and `dotnet build src/src.sln` + `dotnet test` for `tests/DigiByte.Wallet.Tests` and `tests/DigiByte.Api.Tests`
- [ ] T064 Write the PR description: link issue #90 and the spec; note the new `/api/price/range` endpoint (price-proxy role, Principle II), the price archive and its refresh workflow (new GitHub workflow with write permission limited to opening pull requests: flag as security-relevant), and no CSP change

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after Setup. **Blocks every story.** T008 (pure refactor) goes before T014.
- **US1 (Phase 3)**: after Foundational. It is the MVP and delivers the sheet and buttons that US2–US5 extend.
- **US2, US3, US4**: each after US1 (they extend `ExportCsvSheet` and `TransactionExportService`). All edit `TransactionExportService.cs` and `ExportCsvSheet.razor`, so merge them one at a time.
  - US2 is independent of US3 and US4.
  - **US4 needs US3 for DD values outside USD**: T053 calls `/api/price/range?asset=dd` (T042) through `GetDailyPricesAsync` (T044). Built before US3, a DD export still works: USD values are 1.00 and other currencies' Price/Value cells stay empty and are counted (FR-007). Build US3 first, or accept that until US3 lands.
- **US5 (Phase 7)**: after US1; it touches `ExportRowMapper.cs`, which US4 also changes, so do it after US4 or coordinate.
- **Polish (Phase 8)**: after the stories you intend to ship.

### User Story Dependencies

```text
Setup → Foundational → US1 (MVP) ─┬─▶ US2 (ranges)
                                  ├─▶ US3 (prices)     ← also needs the API endpoint T042
                                  ├─▶ US4 (DigiDollar) ─▶ US5 (names)   ← non-USD DD values need US3
                                  └─────────────────────▶ Polish
```

### Within Each User Story

- Tests first, and they must fail before the implementation (Principle III).
- Models (`ExportRequest`, `DailyPriceSeries`) → services (`TransactionExportService`, readers) → UI (`ExportCsvSheet`, page buttons) → quickstart check.

### Parallel Opportunities

- **Phase 2**: T003–T006 (four test files) in parallel; then T009 and T010 in parallel.
- **US1**: T016, T020 and T021 in parallel.
- **US2**: T025, T026, T027 and T031 in parallel.
- **US3**: T034–T038 and T046 in parallel. The price-service side (T039–T042) can be built alongside the wallet side (T043–T044). The one-time seed in T040 needs the maintainer and can happen any time before US3 ships.
- **US4**: T048–T050 in parallel.
- **Polish**: T057–T059 in parallel.

---

## Parallel Example: Foundational phase

```text
Task: "Write CsvFormatTests in tests/DigiByte.Wallet.Tests/Export/CsvFormatTests.cs"            (T003)
Task: "Write TransactionCsvWriterTests in tests/DigiByte.Wallet.Tests/Export/TransactionCsvWriterTests.cs" (T004)
Task: "Write ExportRowMapperTests in tests/DigiByte.Wallet.Tests/Export/ExportRowMapperTests.cs"  (T005)
Task: "Write CompleteHistoryTests in tests/DigiByte.Wallet.Tests/CompleteHistoryTests.cs"         (T006)
```

## Parallel Example: User Story 3

```text
Task: "PriceRangeEndpointTests in tests/DigiByte.Api.Tests/PriceRangeEndpointTests.cs"            (T034)
Task: "DailyPriceSeriesTests in tests/DigiByte.Wallet.Tests/Export/DailyPriceSeriesTests.cs"      (T035)
Task: "Add price strings to wwwroot/locales/en.json and es.json"                                  (T046)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → Phase 2 Foundational.
2. Phase 3 (US1): the full DGB history to CSV, with empty price columns.
3. **Stop and check** quickstart scenarios 1–6, 11, 15, 16, 18 and 20. This alone answers issue #90's main request.

### Incremental Delivery

1. US1 → ship (MVP).
2. US2 (date ranges) → ship. Month-end use.
3. US3 (daily fiat value) → ship. Needs the API deployed before the wallet.
4. US4 (DigiDollar) → ship.
5. US5 (names) → ship.
6. Polish (docs, roadmap, the cross-app check).

### Deployment note

US3 needs `DigiByte.Api` deployed with `/api/price/range` **before** the wallet build that calls it. Until then, the wallet's price request fails and the export still succeeds with empty value columns (FR-007). The order is therefore safe either way.

---

## Notes

- [P] = different files, no dependency on unfinished work.
- **Fail first, in every phase** (Constitution Principle III): before implementing a phase, run that phase's new tests with `dotnet test --filter` and confirm they fail (compile error or assertion). T007 does this explicitly for Phase 2; the same check applies to the test tasks of US1–US5.
- Commit after each task or small group, using Conventional Commits (`feat(export): …`, `test(export): …`).
- Never weaken an existing read path to make the export work; the strict reads are separate (research R1).
- Stop at any checkpoint to check that story on its own.
