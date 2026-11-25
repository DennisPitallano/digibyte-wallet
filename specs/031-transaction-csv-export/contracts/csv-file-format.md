# Contract: Exported CSV file

The file people hand to an accountant. Columns, order and formats are fixed. A change to any of them is a breaking change for bookkeepers' templates.

## File

- **Name**: `digibyte-{wallet-slug}-{DGB|DD}-{yyyy-MM-dd}.csv` (export date, local).
- **Encoding**: UTF-8 with BOM (`EF BB BF`). Line ending CRLF.
- **Separator / decimal mark**, from the device's number format:

  | Device decimal mark | Separator | Decimal | Example amount |
  |---|---|---|---|
  | `.` | `,` | `.` | `-12.50000000` |
  | `,` | `;` | `,` | `-12,50000000` |

- **Line 1** is the header row. No preamble, no footer, no totals row.
- **Rows** are newest first. One row per transaction; no duplicates by Transaction ID.
- **Never written** when the history could not be read completely, or when no row matches. In those cases there is no file at all, not even a header-only one.

## Columns (same order in DGB and DD files)

| # | Header (en) | Header (es) | Content |
|---|---|---|---|
| 1 | Date | Fecha | `yyyy-MM-dd HH:mm:ss`, local time |
| 2 | Time zone | Zona horaria | `UTC+hh:mm` for that row |
| 3 | Asset | Activo | `DGB` / `DD` |
| 4 | Type | Tipo | Received, Sent, Own transfer, DigiDollar transfer, DigiDollar minted, DigiDollar redeemed (localized) |
| 5 | Amount | Importe | Signed; DGB 8 decimals, DD 2 decimals |
| 6 | Fee (DGB) | Comisión (DGB) | Negative; empty unless sent / own transfer |
| 7 | Price ({CUR}, daily) | Precio ({CUR}, diario) | Per 1 unit of the asset; empty when unknown |
| 8 | Value ({CUR}, daily) | Valor ({CUR}, diario) | Amount × price; empty when price unknown |
| 9 | Status | Estado | Confirmed, Unconfirmed, Failed (localized) |
| 10 | Confirmations | Confirmaciones | Integer |
| 11 | Name | Nombre | Contact or wallet name; empty when none |
| 12 | Counterparty address | Dirección de la otra parte | |
| 13 | Wallet address | Dirección de la billetera | |
| 14 | Note | Nota | On-chain note; empty when none |
| 15 | Transaction ID | ID de transacción | |

For currencies converted from USD, the headers of columns 7 and 8 add `, via USD` (es: `, vía USD`), for example `Value (PHP, daily, via USD)`. Each row is converted at that day's rate.

## Cell rules

1. A field containing the separator, `"`, CR or LF is wrapped in `"…"`, with inner `"` doubled.
2. A text field (columns 4, 9, 11–15) starting with `=`, `+`, `-`, `@`, TAB or CR is prefixed with `'`.
3. Numeric fields (5–8, 10) have no thousands separator, no currency symbol and no quotes.
4. An empty value is an empty field (`,,`), never `0`, `-` or `N/A`.

## Sums the file guarantees

- DGB file: Σ Amount + Σ Fee = DGB balance change over the exported rows (excluding Unconfirmed rows if the reader filters them out).
- DD file: Σ Amount = DD balance change; Fee is the DGB spent on DD fees.

## Example (dot locale, USD)

```text
Date,Time zone,Asset,Type,Amount,Fee (DGB),Price (USD daily),Value (USD daily),Status,Confirmations,Name,Counterparty address,Wallet address,Note,Transaction ID
2026-11-20 23:30:05,UTC+08:00,DGB,Received,250.00000000,,0.01120000,2.80,Confirmed,812,Customer – Alice,dgb1q…x7,dgb1q…k2,,9f2c…01
2026-11-18 10:02:44,UTC+08:00,DGB,Sent,-100.00000000,-0.00022600,0.01090000,-1.09,Confirmed,1990,Supplier,DQ4…9a,dgb1q…k2,"Invoice 12, part 1",1ab0…7e
2026-11-17 09:15:00,UTC+08:00,DGB,Own transfer,0.00000000,-0.00011300,0.01080000,0.00,Confirmed,2410,,dgb1q…m1,dgb1q…k2,,77c1…3d
```

(In the real file, the price headers read `Price (USD, daily)`: the comma inside makes them quoted, `"Price (USD, daily)"`. The example drops it for readability.)
