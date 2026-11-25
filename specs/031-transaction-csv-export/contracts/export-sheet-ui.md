# Contract: Export sheet (UI)

One shared component, `ExportCsvSheet`, opened from **Activity** (DGB) and from the **DigiDollar** screen (DD).

## Entry points

| Screen | Control | Opens with |
|---|---|---|
| Activity (`/transactions`) | **Export CSV** button in the header row, next to the filters | `Asset = DGB`, the current filter/search/address passed in for "What the list shows" |
| DigiDollar (`/digidollar`) | **Export CSV** button above the history list | `Asset = DD` |

The button is disabled with its tooltip while the wallet is locked (FR-019).

## Sheet layout (top to bottom)

1. Title: "Export transactions (DGB)" or "(DD)".
2. **What to export** (radio): Everything · What the list shows (DGB only) · Date range.
3. Date range (only when chosen): This month · Last month · This year · Custom (From, To date inputs).
4. Notice (always shown, FR-012): "The file includes full amounts and addresses and is not password-protected. Keep it somewhere safe." When hidden-amounts mode is on, add: "Amounts hidden on screen are shown in the file."
5. Primary button **Export**. Secondary **Cancel**.

Three actions from Activity to a saved file: Export CSV → Export → save/share confirm (SC-001).

## States

| State | Shown |
|---|---|
| Reading (after 1 s) | Progress bar "Reading history… n of m addresses" + **Cancel** |
| Prices | "Getting daily prices…" |
| Saved / Shared | Toast: "Exported N transactions." When some rows lack a price: "N transactions exported. M have no {CUR} value (no price was available for those days)." |
| HistoryUnavailable | Inline error: "Couldn't read your full history from the network, so nothing was exported." + **Try again** |
| NothingToExport | Inline: "Nothing to export for this choice." No file |
| Locked | "Unlock your wallet to export." |
| Cancelled | Sheet closes, nothing saved |

## Text

All strings are added to `wwwroot/locales/en.json` and `es.json` under `export.*` (FR-018).

## Accessibility

The sheet is a dialog with focus trapped and returned to the button on close. Radios are real inputs with labels. The progress bar is labelled and announced via `aria-live="polite"`.
