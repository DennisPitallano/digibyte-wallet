# Feature Specification: Export Transaction History to CSV

**Feature Branch**: `feature/transaction-csv-export` (planned; spec directory `031-transaction-csv-export`)

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "help me address this request: https://github.com/DennisPitallano/digibyte-wallet/issues/90" — *"Please include a CSV export of entire Transaction History. The CSV export feature will become increasingly important for business record keeping purposes as more retailers adopt and accept payment in DGB and DD for over the counter purchases."*

## Context

**What the person sees today.** **Activity** lists the wallet's transactions by day, with filters (All, Received, Sent), a search box and an address picker. It shows 50 at a time with a "show more" button, and reads at most the most recent 1,000 transactions from the chain. Each row can be opened for its detail. There is no way to take the list out of the wallet: a shop owner who wants the month's takings in a spreadsheet has to read amounts off the screen and retype them.

**Who asked.** A retailer-minded user: shops that accept DGB and DigiDollar (DD) over the counter need records for bookkeeping, accountants and tax filing.

**What already exists to build on.** The wallet already knows, for every transaction, its date and time, direction, amount, fee, number of confirmations, transaction id, the wallet's own addresses involved, counterparty addresses and any label or contact name. It already holds a fiat price for the person's chosen currency. Activity already has a "saved history" for when the network is unreachable. The merchant payments service (DigiPay) already offers a CSV download of its own payments; this feature is for the personal wallet and does not change that one.

**Two sets of history.** DGB transactions are listed in Activity. DigiDollar (DD) activity is listed in its own area. A retailer who takes both needs both, as two files with matching columns.

## Clarifications

### Session 2026-11-25

- Q: When the export shows a fiat value for each transaction, which price should it use? → A: The price on the day of each transaction. The wallet fetches one daily price series covering the export's date range, so no single transaction date is sent. Rows the series doesn't cover stay empty.
- Q: If the wallet can't read the full history from the network, should the export stop, or offer to save what the wallet already has stored? → A: Stop. The wallet explains that the network couldn't be reached and offers Try again. No file is saved, because the stored copy holds only the latest transactions.
- Q: Should a shop that takes both DGB and DigiDollar (DD) get one file per currency, or one combined file with both? → A: One file per currency. Activity exports DGB and the DigiDollar area exports DD. Both files use identical columns, including an Asset column, so they can be combined by pasting one under the other.

### Session 2026-11-26

- Q: Which time should each row's date and time be in? → A: The person's local time, written as `2026-11-25 14:03:00`, plus a separate column with the time-zone offset (for example `UTC+08:00`). Days, date ranges and daily prices all follow local time.
- Q: For a sent transaction, how should the amount and fee appear? → A: Amount is signed: positive when received, negative when sent, not including the fee. Fee is its own column, negative, on sent rows only. A transfer between the wallet's own addresses shows Amount 0 and the fee.
- Q: For transactions older than one year, should the export still show a fiat value? → A: Yes. The wallet's price service keeps its own archive of daily DGB prices, filled once with the full price history and then extended with each new day, so every date gets a value.
- Q: For currencies the price source doesn't price DGB in directly, which exchange rate should the export use to convert the daily USD price? → A: Each day's exchange rate. The price service also archives daily USD exchange rates for the converted currencies, filled once from history and refreshed monthly like the price archive.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Download my whole DGB history as a spreadsheet file (Priority: P1)

A shop owner opens **Activity** and presses **Export CSV**. The wallet reads the complete history of the active wallet (not just the 50 rows on screen) and saves a file they can open in Excel, Google Sheets or hand to an accountant. Each row is one transaction with its date and time, whether it was received or sent, the amount, the fee, confirmations and the transaction id.

**Why this priority**: This is the request itself. Without it, nothing else here has value.

**Independent Test**: With a wallet that has several received and sent transactions, press Export CSV and open the saved file in a spreadsheet; every transaction appears once, with correct dates, directions and amounts.

**Acceptance Scenarios**:

1. **Given** a wallet with 120 transactions, **When** the person presses Export CSV, **Then** the file holds all 120 (not only the 50 shown), newest first.
2. **Given** a received and a sent transaction, **When** the file is opened, **Then** the received amount is positive, the sent amount is negative and excludes the fee, and the fee appears as a negative value in its own column on the sent row (and on own-transfer rows), never on the received row.
5. **Given** any export, **When** the person sums the Amount and Fee columns, **Then** the result equals the wallet balance change over the exported transactions.
3. **Given** the file is opened in a spreadsheet, **Then** amounts and dates are read as numbers and dates (not as text), and the header names are plain words.
4. **Given** the active wallet is locked or the full history cannot be read from the network (even if Activity is showing its saved copy), **When** the person presses Export CSV, **Then** they are told why nothing was exported, offered Try again, and no file is saved.

---

### User Story 2 - Export only what I need: dates, direction, address (Priority: P2)

A bookkeeper only needs last month. Before exporting, the person chooses to export **everything**, **what the list currently shows** (its filter, search and address), or a **date range** (This month, Last month, This year, or From–To).

**Why this priority**: Month-end and year-end records are the main use. Exporting everything and trimming in a spreadsheet works, so this follows the basic export.

**Independent Test**: Export with Last month chosen and confirm every row falls inside that month and none outside it.

**Acceptance Scenarios**:

1. **Given** the Sent filter is on, **When** the person chooses "What the list shows", **Then** only sent transactions are in the file.
2. **Given** a From–To range, **When** exported, **Then** the file holds exactly the transactions in that range, inclusive of both days in the person's local time.
3. **Given** the chosen option matches nothing, **When** the person exports, **Then** they are told there is nothing to export and no file is saved.

---

### User Story 3 - Books in my own currency (Priority: P2)

A retailer needs to know what each sale was worth in their local currency on the day it happened. The export includes a fiat value column for the person's chosen currency, priced on the day of each transaction, so the file can be used for sales records without looking prices up by hand.

**Why this priority**: It is what turns a transaction list into a business record. It is not needed to get the data out, so it follows the core.

**Independent Test**: With fiat set to a currency, export two transactions made on different days and confirm each row's fiat value uses that day's price, and the file states the currency and that prices are daily.

**Acceptance Scenarios**:

1. **Given** the person's fiat currency is set, **When** they export, **Then** each row shows its amount valued at that day's price, and the file says which currency it is in.
2. **Given** two transactions of the same amount on days with different prices, **When** exported, **Then** their fiat values differ accordingly.
3. **Given** the daily prices could not be fetched, or do not cover some days, **When** they export, **Then** the file is still produced, the fiat cells for uncovered rows are left empty, and the person is told how many rows have no fiat value.
4. **Given** an export of any range, **Then** the price service is asked only for the range's first and last day, never for the individual transaction dates.
5. **Given** a transaction from three years ago, **When** exported, **Then** its row has a fiat value at that day's price.

---

### User Story 4 - Export DigiDollar (DD) history too (Priority: P2)

A retailer who accepts DD opens DigiDollar activity and exports it the same way. The file shows DD amounts as DD, not mixed up with DGB.

**Why this priority**: The request names DD explicitly. It is separate from the DGB list, so it can ship after it.

**Independent Test**: With DD activity present, export from the DigiDollar area and confirm every row is a DD transaction with amounts in DD.

**Acceptance Scenarios**:

1. **Given** DD transactions, **When** the person presses Export CSV in the DigiDollar area, **Then** the file holds them with the asset named as DD.
2. **Given** both DGB and DD history, **When** the person exports from Activity, **Then** the file holds only DGB rows; exporting from the DigiDollar area gives only DD rows.
3. **Given** a DGB file and a DD file, **When** the person pastes the rows of one under the other in a spreadsheet, **Then** the columns line up and the Asset column tells each row's currency.

---

### User Story 5 - Notes and counterparties for bookkeeping (Priority: P3)

Rows carry the label or contact name the person already gave (for example "Customer – Alice" or "Supplier"), and the wallet address involved, so the file can be matched to sales without opening each transaction.

**Why this priority**: Helpful, but the file is already useful without it.

**Independent Test**: Give a contact a name, export, and confirm the contact's name and address appear on that row.

**Acceptance Scenarios**:

1. **Given** a transaction to a saved contact, **When** exported, **Then** the row shows the contact's name and address.
2. **Given** an address with no name, **Then** the name column is empty, never filled with a placeholder.

---

### Edge Cases

- **Very long histories.** A history longer than what Activity loads today (1,000) is still exported in full; the person sees progress and can cancel. (FR-003)
- **Network unreachable.** If the full history can't be read, the export stops with an explanation and Try again, even when Activity is showing its saved copy. No partial file is ever saved. A read that fails partway (for example during a long "Everything" export) also stops without saving. (FR-006)
- **Names that start with `=`, `+`, `-` or `@`.** A label or contact name could be crafted to run as a formula when opened in a spreadsheet; such cells are made safe. (FR-009)
- **Commas, quotes and line breaks** in names and notes do not break the columns. (FR-008)
- **Unconfirmed transactions** are included and marked as unconfirmed. (FR-005)
- **A DigiDollar send that failed** is listed with status Failed and Amount 0, so it is never counted as spending. (FR-005)
- **Transfers between the wallet's own addresses** (consolidation, change-only) are shown as such, with Amount 0 and only their fee, so they are never counted as income or spending. (FR-005)
- **Several wallets.** Only the active wallet is exported; the file name says which. (FR-011)
- **Hidden-amounts mode** (the on-screen privacy toggle) does not apply to a file the person deliberately saves, but they are told the file will contain real amounts. (FR-012)
- **Near midnight.** A transaction at 23:30 local time belongs to that local day for the date range, the daily price and monthly totals, even though it falls on the next day in UTC. (FR-004, FR-007)
- **Daylight-saving change.** Rows on either side of a clock change carry their own offset, so the offset column can differ within one file. (FR-004)
- **Weekend in a converted currency.** A Saturday sale in a currency converted from USD uses Saturday's DGB price and Friday's exchange rate, since rates aren't published at weekends. (FR-007)
- **Locale.** A person whose spreadsheet uses a comma as the decimal separator can still open the file correctly. (FR-010)
- **Browser, installed app and phone.** The file is saved or shared with the usual save/share behavior of each. (FR-013)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Activity MUST offer an **Export CSV** action that saves the active wallet's transaction history as a CSV file.
- **FR-002**: The person MUST be able to choose what is exported: everything, what the list currently shows (filter, search, address), or a date range (This month, Last month, This year, custom From–To).
- **FR-003**: "Everything" MUST include the wallet's complete history, not only the rows shown or the most recent 1,000, and MUST show progress and let the person cancel once reading has taken more than 1 second.
- **FR-004**: Each row MUST be one transaction and MUST include: date and time in the person's local time (as `YYYY-MM-DD HH:MM:SS`), the time-zone offset in its own column (as `UTC+HH:MM`), asset (DGB or DD), direction (received, sent, or between own addresses), amount, fee (sent only), confirmations or status, transaction id, the wallet's own address involved, and the counterparty address.
- **FR-005**: Amount MUST be signed: positive when received, negative when sent, never including the fee. Fee MUST be its own column, negative, filled on sent and own-transfer rows only, and always in DGB (DigiDollar transactions pay their network fee in DGB), so in a DGB file Amount plus Fee sums to the balance change, and in a DD file Amount alone does. Transfers between the wallet's own addresses MUST be marked as such, with Amount 0 and their fee. Unconfirmed transactions MUST be included and marked as such.
- **FR-006**: When the history needed for the export cannot be read completely from the network, including a failure partway through, the wallet MUST stop, explain that the network couldn't be reached, offer Try again, and MUST NOT save any file. The saved copy Activity shows offline MUST NOT be used as an export source.
- **FR-007**: The file MUST include a fiat value per row in the person's chosen currency, using the daily price for the day of that transaction (in the person's local time), and MUST state the currency and that prices are daily. Daily prices MUST be requested once as a series covering the export's date range, never per transaction date. The series MUST cover every day from the start of DGB price data to yesterday, however old, served from the price service's own daily archive plus the latest days from its live source. For a currency that DGB is not priced in directly, the value MUST be the day's USD price converted at that same day's USD exchange rate; on a day with no published rate (weekends, holidays) the most recent earlier rate up to 7 days old is used. Rows the series still does not cover (a day missing from both) MUST have empty fiat cells; the person MUST be told how many rows have no fiat value; the export MUST still succeed.
- **FR-008**: Values containing separators, quotes or line breaks MUST be escaped so columns stay aligned in common spreadsheet programs.
- **FR-009**: Text that a spreadsheet would run as a formula MUST be neutralised so exporting never creates a file that executes something when opened.
- **FR-010**: The file MUST open correctly, with accented and non-Latin names intact, and numbers and dates read as numbers and dates, in Excel, Google Sheets and LibreOffice, whatever the person's regional decimal setting.
- **FR-011**: The file name MUST identify the wallet, the asset (DGB or DD) and the date of export. Only the active wallet MUST be exported.
- **FR-012**: Before saving, the wallet MUST tell the person that the file contains full amounts and addresses and is not protected; hidden-amounts mode MUST NOT blank the contents of a file the person chose to save.
- **FR-013**: Saving MUST work everywhere the wallet runs (desktop browser, installed app, phone) using that place's normal save or share behavior.
- **FR-014**: Exports MUST be one currency per file: Activity exports DGB only, and the DigiDollar area MUST offer the same export for DD only, with amounts in DD. Both files MUST use the same columns in the same order, so their rows can be combined by pasting. There is no combined DGB+DD export in this release.
- **FR-015**: A row MUST show the label or contact name the person gave to the counterparty or transaction, when one exists; otherwise that cell is empty.
- **FR-016**: Exporting MUST NOT send history, addresses or amounts to any server; the file is built on the person's device. The only outside requests are the daily price series in FR-007, which carries the currency, the asset (DGB or DD) and the range's first and last day (widened by one day on each side) and nothing else, and nothing else: exchange-rate conversion happens in the price service as part of that same request.
- **FR-017**: With nothing to export (no transactions, or the choice matches none), the wallet MUST tell the person and MUST NOT save an empty or header-only file.
- **FR-018**: All new text MUST be available in every language the wallet offers (English and Spanish).
- **FR-019**: A locked wallet MUST NOT export; the person is asked to unlock first.

### Key Entities

- **Export request**: what to export (everything, current view or date range) and which asset (DGB or DD). Fiat values are always included.
- **Export row**: one transaction as the person's records need it — time, direction, amount, fee, fiat value, status, ids, addresses, names.
- **Daily price series**: one price per day in the person's currency, covering the export's date range; used to value each row.
- **Export file**: the saved CSV, carrying a header row, the currency used, and a name that identifies wallet, asset and export date.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person can go from Activity to a saved file in no more than 3 actions (open the export choices, confirm, save).
- **SC-002**: 100% of the wallet's transactions are present exactly once in an "Everything" export, checked against the on-chain history of a test wallet with more than 1,000 transactions.
- **SC-003**: A history of 1,000 transactions is exported in under 10 seconds on a typical laptop on a normal connection.
- **SC-004**: The same file opens with correct columns, dates, numbers and names in Excel, Google Sheets and LibreOffice with no manual import steps.
- **SC-005**: Over a test month, the file's received total and sent total match the totals the person gets by adding up the same days in Activity, to the last digit, and Amount plus Fee summed over the month equals the wallet's balance change for that month.
- **SC-006**: No test label or contact name is able to run as a formula when the file is opened.
- **SC-007**: A bookkeeper can produce last month's sales record from the wallet in under 1 minute without retyping any amount.

## Assumptions

- **Format**: A plain CSV file, one header row, UTF-8, local dates and times in the form given in FR-004 so spreadsheets read them as dates. The separator and decimal mark follow the device's number format (comma and dot, or semicolon and comma) so the file opens in columns in every region (FR-010; see research R4). Other formats (PDF statements, XLSX, accounting-software formats) are out of scope.
- **Fiat value** uses a daily price (one price per calendar day), not the price at the exact minute of a transaction. The wallet's own price service is extended to give daily DGB prices for a date range. Its live source only covers the last 365 days, so the service also keeps a daily price archive, filled once from the full price history and kept up to date on a schedule; together they cover every date. The service also archives daily USD exchange rates for the currencies it converts, the same way. A converted currency's rates start on the first day a free daily-rate history exists for it; earlier rows have no value and are counted in the message. DD is valued at its USD peg (1 DD = 1 USD), converted at each day's rate for other currencies.
- **Multisig shared wallets** have their own activity; exporting those is out of scope here and can follow the same pattern later.
- **Address book export**, already on the roadmap as a separate line, is out of scope.
- **The merchant payments service (DigiPay)** keeps its own CSV download and is unchanged.
- The wallet's existing history loading and saved history are reused; the daily price series is the only new data source.
- Export is always started by the person; there are no scheduled or automatic exports.
