# Contract: Receive address list helper and page behavior

No external API. These are the internal contracts between the wallet library and the Receive page.

## `ReceiveAddressList` (DigiByte.Wallet, static, pure)

```text
Build(addressesNewestFirst, history, labelLookup, onShowAddress) -> IReadOnlyList<ReceiveAddressEntry>
    addressesNewestFirst: (Index, Address) pairs already derived, newest first
    history: the loaded transactions, or null when no history is available (every Status = Unknown)
    labelLookup: label by normalized address (as ActivityAddressIndex.BuildLabelLookup builds it)
    onShowAddress: the address in the QR code, to set IsOnShow (compared in normalized form)

StatusOf(address, history) -> (Status, TransactionCount)
    Status and count of one address; the same rules as Build; Unknown when history is null

NextWindow(currentCount, generatedCount) -> int
    Size of the list after "Show more": min(currentCount + 10, generatedCount)

WindowStart(currentIndex, shownCount) -> (startIndex, count)
    The index range to derive for the newest `shownCount` addresses (never below 0)
```

Guarantees: no I/O; never throws on empty input; `Unknown` is returned whenever `history` is null; used/unused comparison is encoding-independent (`AddressKey`).

## Page behavior (Receive.razor, HD wallets only)

- On open: the newest address is on show; a folded "Your addresses (N)" row is shown (folded on every visit, state not saved) and the opened list shows the newest 10; names load once; status comes from Home's cached history, or a one-time background load when there is none.
- Choosing an entry: sets the on-show address, rebuilds the QR/payload and the name box; keeps the payment amount and note; saves nothing.
- "Show more": derives the next 10 and shows them; hidden when all are shown.
- Name control per entry and the existing name box: both write the same label; both refresh the list.
- "Generate new address": unchanged call; on success the new address is added on top and shown; on failure the existing message appears and nothing changes.
- A short notice under the QR code appears only when the address on show is `Used` (never for `Unused` or `Unknown`) and never blocks copy, share or download.
- "View activity": shown only on `Used` entries; writes `pref:activityAddress:<walletId>` and opens `/transactions`.
- Single-key (WIF) wallets: none of this is shown; the legacy/SegWit switch is unchanged.

## Behaviors checked by tests

- Entries are newest first and the top entry equals the address Receive shows; after generating one new address the list has one more entry on top.
- `Unknown` for every entry when history is null (the page shows it as a muted "Status unknown" label); `Unused` only with a loaded history.
- A bech32 checksum variant in the history still marks the address `Used`.
- `NextWindow` stops at the number of generated addresses; `WindowStart` never goes below index 0.
