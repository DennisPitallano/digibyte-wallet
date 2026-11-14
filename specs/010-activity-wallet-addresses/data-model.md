# Data Model: Wallet Addresses in Activity

## TransactionRecord (existing, extended)

Existing fields are unchanged. New optional members:

| Field | Meaning | Rules |
|---|---|---|
| `WalletAddresses` | The user's own addresses that this transaction ties to, in display order. Received: addresses that were paid. Sent: addresses that funded it. | Empty when unknown. No duplicates (compared in normalized form). Never contains change-only addresses (FR-007). |
| `WalletReceivedAddresses` | Sent transactions only: the user's own non-change addresses that the transaction paid (a transfer between own addresses). | Empty for received transactions and ordinary sends. |

- First entry of `WalletAddresses` is the one shown on the row; the rest feed "+N more" (FR-001).
- Old stored records have both empty; they deserialize unchanged.
- When a locally tracked send is merged with the explorer's version, the addresses found by the explorer are copied onto the local record and saved with it.
- A transaction matches an address filter if the address is in `WalletAddresses` or `WalletReceivedAddresses` (FR-008, self-transfer clarification).

## UsedAddress (new, derived, not stored)

| Field | Meaning |
|---|---|
| `Address` | Address as the wallet generated it (display form) |
| `Key` | Normalized form used for grouping and filtering |
| `Label` | User label from `AddressLabelService`, if any |
| `TransactionCount` | Number of listed transactions that match this address (either list) |
| `LastUsed` | Newest matching transaction time; list sorted by this, newest first |

Rules: one entry per normalized address; only addresses with at least one transaction (pending included) appear (FR-004); change-only addresses never appear.

## Activity filter state (page state, not stored)

`filter` (all/received/sent), `query` (search text), `selectedAddressKey` (null = all). They combine with AND (FR-006). Search text also matches the wallet addresses (FR-009). The chosen address (not the tabs or search text) is remembered per wallet as a small preference and restored on the next visit if that address still appears in the activity (FR-011). Labels edited in the picker use the same label store as the Receive page (FR-012).
