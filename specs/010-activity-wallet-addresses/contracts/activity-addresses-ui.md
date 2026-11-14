# Contract: Activity address helper and UI inputs

The feature has no external API. These are the internal contracts between the wallet library and the pages.

## `ActivityAddressIndex` (DigiByte.Wallet, static, pure)

```text
Build(transactions, labels) -> IReadOnlyList<UsedAddress>
    labels: lookup from address to label text (may be empty)
    Result sorted by LastUsed descending; one entry per normalized address;
    only addresses referenced by at least one transaction.

Matches(transaction, addressKey) -> bool
    true when addressKey is null/empty (no filter),
    or the normalized key is in WalletAddresses or WalletReceivedAddresses.

Display(transaction, labels) -> (Primary, MoreCount)
    Primary: label of first wallet address, else the address (null when none known).
    MoreCount: number of other wallet addresses (0 when none).
```

Guarantees: never throws on empty/old data; address comparison is encoding-independent; no I/O.

## Component inputs

- `TransactionRow`: new optional parameters `WalletAddressText` (string?) and `WalletAddressMore` (int). When the text is null nothing extra is rendered.
- `TransactionDetailModal`: reads `Tx.WalletAddresses` / `Tx.WalletReceivedAddresses`; each entry shows its label, the full address and a copy action.
- `Transactions` page: filter bar gains an address picker bound to `selectedAddressKey`, with a one-action clear. An empty result keeps the existing empty-state with a way to clear the address filter.

## Behaviors checked by tests

- Filter by address X with "Received" active returns only received transactions that include X.
- Transfer between own addresses A and B appears once and matches filters A and B.
- Change-only address never appears in `Build`.
- Records with no address data are listed without address text and match no address filter.
