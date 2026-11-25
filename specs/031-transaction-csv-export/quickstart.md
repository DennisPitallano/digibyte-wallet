# Quickstart: validating the CSV export

How to prove the feature works end to end. Formats and columns: [contracts/csv-file-format.md](contracts/csv-file-format.md). Sheet behaviour: [contracts/export-sheet-ui.md](contracts/export-sheet-ui.md).

## Prerequisites

- .NET 10 SDK, the repo building with `dotnet build src/src.sln`.
- The wallet and price API running locally (the `.claude/launch.json` entries, or `dotnet run` for `DigiByte.Web` and `DigiByte.Api`).
- A **testnet** wallet with at least: one receive, one send to a saved contact, one transfer between its own addresses, one send with a note, and one unconfirmed transaction. For SC-002, a wallet (mainnet, read-only watch is fine) with more than 1,000 transactions, or an address with more than 2,000.
- Excel, Google Sheets and LibreOffice Calc for SC-004.

## 1. Automated tests

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Export"
```

```powershell
dotnet test tests/DigiByte.Api.Tests --filter "FullyQualifiedName~PriceRange"
```

Expected: all pass. They cover quoting, formula neutralising, both separator styles, signs and fees (R6 table), local time and DST offsets, range presets, daily-price lookup by local date, empty prices, the stop-on-partial-history rule, and the endpoint's validation and daily reduction.

## 2. Manual scenarios

| # | Do | Expect | Spec |
|---|---|---|---|
| 1 | Activity → Export CSV → Everything → Export, counting each click or tap | File `digibyte-{wallet}-DGB-{today}.csv` saved in no more than 3 actions; row count = all transactions, not 50 | US1, FR-001/003/011, SC-001 |
| 1b | Export Everything from a wallet with more than 1,000 transactions; compare the row count with the transaction count an explorer shows for the wallet's addresses | Equal, and no Transaction ID appears twice | SC-002 |
| 2 | Open it in Excel, Sheets, LibreOffice | Columns split; dates are dates; amounts are numbers; accented names intact | FR-010, SC-004 |
| 3 | Check the send row | Amount negative excluding fee; Fee negative in its own column; Name = contact | FR-005/015 |
| 4 | Check the own-address transfer | Type "Own transfer", Amount 0, Fee present | FR-005 |
| 5 | `=SUM(Amount)+SUM(Fee)` over a month vs the month's balance change | Equal to the last digit | SC-005 |
| 6 | Rename a contact to `=HYPERLINK("x")`, export, open in Excel | Shown as text, nothing runs | FR-009, SC-006 |
| 7 | Set the filter to Sent → Export → What the list shows | Only sent rows | US2 |
| 8 | Date range → Last month | Only rows from last month, local days inclusive | US2 |
| 9 | Fiat = USD; export two days with different prices | Different Price per day; header `Price (USD, daily)` | US3, FR-007 |
| 9b | Export a range that includes a transaction more than one year old | That row has a Price and Value from the archive | US3 scenario 5 |
| 10 | Stop the price API, export | File saved, Price/Value empty, toast states how many rows lack a value | FR-007 |
| 11 | DevTools → Offline, export | "Couldn't read your full history…", Try again, no file | FR-006 |
| 12 | Choose a date range with no transactions | "Nothing to export", no file | FR-017 |
| 13 | DigiDollar → Export CSV | DD file; same columns as the DGB file; Fee in DGB | US4, FR-014 |
| 14 | Paste the DD rows under the DGB rows in a sheet | Columns line up | US4 scenario 3 |
| 15 | Switch the OS region to Spain/Germany, export, open in Excel | Semicolon file with comma decimals opens in columns | FR-010 |
| 16 | Phone (or DevTools mobile emulation with touch) | Share sheet offers the CSV file | FR-013 |
| 17 | Switch the app to Spanish | Headers, types and messages in Spanish | FR-018 |
| 18 | Lock the wallet | Export button disabled with "Unlock your wallet to export" | FR-019 |
| 19 | DevTools → Network during export | Only one `/api/price/range` request leaves the device, also for converted currencies; the query has the currency, the asset and two dates only | FR-016 |
| 20 | Large wallet, export Everything | Progress appears after 1 s; Cancel works; 1,000 transactions finish in under 10 s | FR-003, SC-003 |
