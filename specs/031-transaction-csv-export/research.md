# Research: Export Transaction History to CSV

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-26

Each section is one decision the spec left open, resolved against the current code.

---

## R1. Reading the complete DGB history

**Finding.** Activity calls `WalletService.LoadTransactionHistoryAsync(0, 1000)`. Below it:

- `FallbackBlockchainService.GetAddressTransactionsAsync` reads `int.MaxValue` and caches the whole list per address, then applies skip/take.
- The Esplora reader (`BlockchainApiService`) pages 25 at a time but stops at `MaxTransactionsPerAddress = 2000`. The change-only sync (`WalletHistorySync`) has the same 2,000 cap.
- The Blockbook reader asks for `page=1&pageSize={take}`. Blockbook caps the page size at 1,000, so one address with more than 1,000 transactions is cut short.
- Both readers **swallow errors and return an empty list**, so an address that could not be read looks like an address with no history.
- `LoadTransactionHistoryAsync` finally applies `.Take(take)` to the merged list.

So today's path can silently drop transactions in three places. That is fine for a screen but breaks FR-003, FR-006 and SC-002.

**Decision.** Add a separate, strict read for export: `WalletService.LoadCompleteHistoryAsync(IProgress<HistoryExportProgress>, CancellationToken)`.

- It reuses the same address list, the same `TransactionRecord` building (direction, amount, counterparty, DD summary, wallet addresses) and the same merge with locally tracked transactions. The record-building block is pulled out of `LoadTransactionHistoryAsync` into one private method that both call, so the two can never disagree.
- It reads each address through a new `IBlockchainService.GetCompleteAddressHistoryAsync(address, ct)`. That call pages to the end with no per-address cap and **throws** on any failed page instead of returning `[]`. Esplora uses its existing `after_txid` cursor; Blockbook uses `page=N&pageSize=1000` until `page == totalPages`.
- The new reader method has a "not supported" default, so the 30 existing implementers keep compiling. `FallbackBlockchainService` skips a backend that doesn't support it (the node, which cannot list full history), and fails only when no supporting explorer could answer.
- The gateway's one-pass `IWalletHistorySource` is used when it is active, as today (it is already complete by contract).
- Any failure, including a failure partway through, makes the whole call throw `HistoryIncompleteException`. The export then stops with Try again (FR-006). Saved snapshots and the change-only cache are never used as an export source.
- Progress is reported as addresses read / total addresses, so the sheet can show a bar and Cancel (FR-003).

**Alternatives considered.**
- *Call the existing method with `take: int.MaxValue`.* Rejected: it keeps the 2,000 / 1,000 caps and the silent `[]` on errors.
- *Remove the caps from the existing readers.* Rejected: Activity, balance and change-only sync rely on them for speed, and changing their failure behaviour is out of scope.

---

## R2. Reading the complete DigiDollar history

**Finding.** `DigiDollarService.FetchAsync` asks the NodeApi for `HistoryPageSize = 50` entries per address chunk and ignores the `next` cursor that `DigiDollarNodeApiClient.GetHistoryAsync` already returns. The DD screen therefore shows only recent history. `Classify` already turns entries into `DigiDollarTransaction` with direction `Received`, `Sent` or `SelfTransfer`, net `AmountCents`, and `FeeSats` (in DGB).

**Decision.** Add `DigiDollarService.LoadCompleteHistoryAsync(IProgress, CancellationToken)`. For each address chunk it follows `next` until it is null, with a page size of 200 (the largest the client already uses). It then merges pending sends through the same `BuildHistory` / `Classify` code. Any failed page throws, and the export stops (FR-006). The cached last-50 history is never exported.

**Alternatives considered.** *Raise `HistoryPageSize`.* Rejected: it still has no end condition, and it would slow every DD dashboard refresh.

---

## R3. Daily prices for a date range

**Finding.** The wallet only reads today's price: `GetDgbPriceAsync` calls the project's price proxy `/api/price/simple` (`DigiByte.Api/Endpoints/PriceEndpoints.cs`), or CoinGecko directly when no proxy is set. There is no historical price anywhere in the codebase.

**Decision.** Add one proxy endpoint, `GET /api/price/range?currency={code}&from={yyyy-MM-dd}&to={yyyy-MM-dd}`. It calls CoinGecko `coins/digibyte/market_chart/range` once and reduces the points to **one price per UTC calendar day** (the last point of each day). The result is cached for 1 hour per (currency, from, to). Contract: [contracts/price-range-api.md](contracts/price-range-api.md).

- The wallet asks once per export for the range's first and last local day, widened by one day on each side so that local days that straddle UTC are covered (FR-007, FR-016). No transaction dates are sent.
- A row's price is the series entry whose date equals the row's **local** calendar date (FR-007, clarification Q4). The series is keyed by date only, so a 23:30 local transaction uses that local day's price even though it falls on the next UTC day. Rows with no entry stay empty, and the sheet reports how many (FR-007).
- In direct mode (no proxy configured), the wallet calls the same CoinGecko endpoint itself. `https://api.coingecko.com` is already in the wallet's `connect-src`, so **no CSP change** is needed.
- The new client method `GetDailyPricesAsync(currency, fromDay, toDay, ct)` lives in `DigiByte.Wallet` next to `GetDgbPriceAsync` (Principle IV: price fetching lives in the lowest layer that owns it).

**Older dates: a daily price archive (clarification Q6).** CoinGecko's free tier only serves the **last 365 days**, so the proxy also keeps its own archive. `DigiByte.Api` has no database and its hosting does not keep files between deploys, so the archive is **data shipped with the service**: one file per currency, `src/DigiByte.Api/PriceArchive/dgb-daily-{currency}.csv`, with lines `yyyy-MM-dd,price` (UTC day, closing price), copied into the build output.

- **Seeding (one time).** The maintainer downloads CoinGecko's full DGB historical-data export for each currency CoinGecko prices DGB in natively. A small console tool, `tools/PriceArchiveImport`, normalises each export to the archive format. The files are committed. No paid key is needed.
- **Keeping it current.** A scheduled GitHub workflow, `.github/workflows/price-archive-refresh.yml`, runs monthly. For each archived currency it calls the free `market_chart?days=365` endpoint, appends the days after the file's last date, and opens a pull request. The 365-day window overlaps by about 11 months, so a skipped month never leaves a gap. If no refresh has merged for more than 300 days, the workflow fails loudly.
- **Serving.** `/api/price/range` answers from the archive for days it has. For days after the archive's last date it adds the live CoinGecko answer, as before. Where both have a day, the archive wins, so old rows never change between exports. When CoinGecko fails, archived days are still returned. Older exports therefore work even during a CoinGecko outage.
- **Size.** About 4,300 days × ~40 bytes ≈ 170 KB per currency, loaded once per process and kept in memory. Only the proxy downloads it; the wallet never does.
- **Currencies.** DGB prices are archived for CoinGecko-native currencies. Converted currencies use the archived USD price × that day's archived exchange rate (below).

**Alternatives considered.**
- *Accept the 365-day limit.* Rejected in clarification Q6: a January export of last year's books would lose early-January rows.
- *A paid data plan.* Rejected for ongoing cost and key handling.
- *Ship the archive in the wallet.* Rejected: it adds megabytes to the PWA for a rarely used feature.

**Currencies CoinGecko does not price DGB in** (`FiatCurrencies.IsCoinGeckoNative == false`; clarification Q7). Conversion happens **in the proxy**, inside the same `/api/price/range` request, so the wallet sends nothing extra (FR-016):

- **Rate archive.** `src/DigiByte.Api/PriceArchive/usd-fx-{currency}.csv`, lines `yyyy-MM-dd,rate` (units of the currency per 1 USD), one per published day. It is seeded and refreshed exactly like the price archive, by the same import tool and monthly workflow.
- **Sources (free, no key).**
  - The European Central Bank daily reference rates, through the Frankfurter API (`api.frankfurter.app/{from}..{to}?from=USD`), cover about 30 currencies back to 1999.
  - For converted currencies the ECB does not publish, the free daily `exchange-api` snapshots (`cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{date}`) are used; they start in 2024.
  - The seed task records each currency's source and first date in `PriceArchive/README.md`. Rows before a currency's first date have no value and are counted in the message (spec Assumptions).
- **Days without a rate.** The ECB publishes no weekend or holiday rates, so a day uses the most recent earlier rate up to 7 days old. Beyond 7 days the row has no value.
- **Live days.** For days after the rate archive's last date, the proxy calls the same source live, in the same request.
- **Header.** Converted values are marked in the header, for example `Value (PHP, daily, via USD)`, so an accountant knows the conversion route.
- **The old `/api/price/fx` (today's rate) is not used by the export.**

**DigiDollar.** DD is pegged 1 DD = 1 USD. For USD no request is made. For any other currency the wallet requests `/api/price/range?asset=dd`, which returns that day's USD rate (the 1 USD price converted), for native and converted currencies alike.

**Alternatives considered.**
- *One request per transaction date (`/coins/digibyte/history?date=`).* Rejected: it reveals each date (contrary to the clarification) and hits rate limits within a few dozen rows.
- *Today's price for every row.* Rejected in clarification Q1.

---

## R4. File format that opens correctly everywhere (FR-008, FR-009, FR-010)

**Decisions.**
- **Encoding**: UTF-8 with a byte-order mark. Excel needs the BOM to read accented and non-Latin names; Google Sheets and LibreOffice ignore it.
- **Separator and decimal mark follow the device's number format.** The browser's `Intl.NumberFormat` decimal mark is read once.
  - Dot locales (en-US, en-PH, es-MX…): comma separator, dot decimal.
  - Comma locales (es-ES, de, fr, pt-BR…): **semicolon separator, comma decimal**.

  This is the only combination that Excel opens without an import wizard in each region. Google Sheets and LibreOffice detect either. This refines the spec's earlier "dot decimal" assumption (updated in spec.md).
- **No preamble lines**: the header row is line 1, so files paste together cleanly (clarification Q3). The currency and price basis go in the value column's header instead.
- **Numbers**: no thousands separators. DGB has 8 decimals and DD has 2 decimals. Fiat values use the currency's decimals from `FiatCurrencies`.
- **Dates**: `yyyy-MM-dd HH:mm:ss` in local time, plus a `UTC+hh:mm` column computed per row, so a daylight-saving change gives two different offsets in one file (clarification Q4). All three programs read this as a date in every locale.
- **Quoting**: RFC 4180. A field is wrapped in double quotes when it contains the separator, a quote, CR or LF, and inner quotes are doubled.
- **Formula neutralising**: any *text* field (name, note, address) whose first character is `=`, `+`, `-`, `@`, tab or CR gets a leading `'`. Numeric fields are written by the exporter itself, never from user text, and are not changed, so `-1.5` stays a number.

**Alternatives considered.** *`sep=,` first line.* Rejected: Excel then ignores the BOM and breaks non-Latin names, and the extra line breaks pasting. *Always comma + dot.* Rejected: it opens as one column in Excel for comma-decimal regions, which fails FR-010.

---

## R5. Saving the file on each platform (FR-013)

**Finding.** `wwwroot/js/download.js` already has `downloadTextFile` (anchor + Blob) and a `navigator.share` pattern in `shareQrCard`.

**Decision.** Add `saveCsvFile(content, filename)` to `download.js`. It returns `saved`, `shared` or `cancelled`.
- When the main pointer is coarse **and** `navigator.canShare({ files: [file] })` is true (phones and installed mobile apps), it uses the share sheet with a `text/csv` file. iOS standalone apps cannot follow download links reliably.
- Otherwise it uses an anchor download with `text/csv;charset=utf-8`. The BOM is added in C#, so the content is exact.

**Alternatives considered.** *File System Access API (`showSaveFilePicker`).* Rejected: Chromium-only, and not needed.

---

## R6. Rows, signs and types

**Decisions** (from clarifications Q4 and Q5, mapped to the models):

| Source | Type column | Amount | Fee (DGB) |
|---|---|---|---|
| `TransactionRecord` Received | Received | `+AmountSatoshis` | empty |
| `TransactionRecord` Sent, external outputs > 0 | Sent | `−AmountSatoshis` (excludes fee, as today's record already does) | `−FeeSatoshis` |
| `TransactionRecord` Sent, `AmountSatoshis == 0` | Own transfer | `0` | `−FeeSatoshis` |
| `TransactionRecord` with `DigiDollar != null` (DGB file) | DigiDollar transfer / minted / redeemed (`DigiDollarTxSummary.Label`) | DGB moved, as above | as above |
| `DigiDollarTransaction` Received | Received | `+AmountCents/100` | empty |
| `DigiDollarTransaction` Sent | Sent | `−AmountCents/100` | `−FeeSats` (in DGB) |
| `DigiDollarTransaction` SelfTransfer | Own transfer | `0` | `−FeeSats` (in DGB) |

- The fee column is **always in DGB**, in both files, because DD transactions pay their network fee in DGB. For the DGB file, Amount + Fee sums to the DGB balance change (SC-005). For the DD file, Amount alone sums to the DD balance change. The spec's FR-005 now says so.
- **Status**: `Confirmed` when confirmations > 0, else `Unconfirmed`. A DD send that failed (`DdTxStatus.Failed`) is included as `Failed` with Amount 0, so it is never counted.
- **Name**: from `ContactService.GetAllWithWalletsAsync` (contact and own-wallet names), matched on the counterparty address. Otherwise empty (FR-015).
- **Wallet address**: the first of `WalletAddresses`, with its label if the wallet has one.
- **Note**: `Memo` (the OP_RETURN note), when present.

---

## R7. Where the work lives (Principle IV)

| Concern | Project | Why |
|---|---|---|
| Complete history reads, row building, CSV writing, range and daily-price lookup | `DigiByte.Wallet` (`Services/Export/`) | Pure, testable logic; depends on `DigiByte.Crypto` only |
| Daily price series endpoint | `DigiByte.Api` (`PriceEndpoints`) | Existing price proxy role (Principle II) |
| Sheet UI, buttons, saving | `DigiByte.Web` | Presentation only |

No new project, no new reference direction, no new third-party package.

---

## R8. Small open details settled here

- **"More than a moment" (FR-003)**: the progress bar and Cancel appear when the read has not finished after **1 second**.
- **File name**: `digibyte-{wallet-name-slug}-{DGB|DD}-{yyyy-MM-dd}.csv`. The slug is lower-case ASCII letters, digits and dashes; when nothing is left after slugging it becomes `wallet`.
- **Header and Type words** follow the app language (FR-018). Pasting an English file under a Spanish one is not a goal.
- **Rows are newest first**, matching Activity.
- **The FR-012 notice** is a line of text in the export sheet, always shown. It is not a separate confirm, so SC-001 stays at three actions.
