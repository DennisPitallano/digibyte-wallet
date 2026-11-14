# Data Model: Your Addresses on the Receive Page

Nothing new is stored. These are in-memory shapes for one visit to Receive.

## ReceiveAddressEntry (new, derived)

| Field | Meaning | Rules |
|---|---|---|
| `Index` | Position in generation order (0 = first address) | 0 to the wallet's current receive index |
| `Address` | SegWit receiving address as the wallet generated it | Never user-entered |
| `Label` | The user's name for it, if any | From the label store; at most what the label store allows |
| `Status` | `Used`, `Unused` or `Unknown` | `Unknown` unless a history was loaded (see below) |
| `TransactionCount` | Transactions that involve it | Only meaningful when `Used` |
| `IsOnShow` | This is the address in the QR code now | Exactly one entry when the on-show address is in the window |

**Status rules (clarified)**: with a loaded history, an address found in it is `Used` (count from the history) and one not found is `Unused`. With no history yet, or a failed load, every entry is `Unknown`. One exception: if a history was loaded but none of its records carries wallet address data (records cached before spec 010), every entry is `Unknown`, because a missing address could simply be missing data. `Unknown` is never presented as `Unused` (FR-003). The same address in two bech32 checksum forms counts as one.

## Window (page state)

| Field | Meaning | Rules |
|---|---|---|
| `ShownCount` | How many entries are listed | Starts at 10; "Show more" adds 10 until `Index` 0 is listed, then the control is gone |
| `ListOpen` | Whether the "Your addresses (N)" row is open | Starts folded on every visit; never saved |
| `OnShowIndex` | Which address is in the QR code | Starts at the newest (current) index each visit; changing it is never saved (FR-012) |

## Status of one address

`StatusOf(address, history)` gives the status and transaction count of a single address with the same rules as the list entries. The privacy notice (FR-013) uses it for the address on show, so it works while the list is folded and no entry has been derived. With a null history it returns `Unknown`.

## Relationships

- `ReceiveAddressEntry.Label` and `Status` are read from the label store and from the history (`TransactionRecord.WalletAddresses` / `WalletReceivedAddresses`, spec 010) through `ActivityAddressIndex`.
- Generating a new address adds one entry at the top and sets `OnShowIndex` to it. A failure adds nothing and leaves `OnShowIndex` as it was.
- A name set in the list or in the box under the QR code is the same stored label.
