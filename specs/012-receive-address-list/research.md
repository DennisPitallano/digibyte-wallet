# Research: Your Addresses on the Receive Page

No "NEEDS CLARIFICATION" items remain after the eight clarification answers. These are the facts from the code and the decisions that follow.

## R1. Which addresses are "generated", and which is the newest?

**Finding**: Receive shows the address at `ActiveWallet.NextReceivingIndex` (`GetReceivingAddressAsync`). `GenerateNewReceivingAddressAsync` moves that index forward (past any index the chain shows as used) and saves it. Address discovery can also raise `UsedReceivingCount`, and the next new index is `max(NextReceivingIndex + 1, UsedReceivingCount)`.

**Decision**: The generated addresses are indexes `0..NextReceivingIndex`; the newest is the one Receive shows now. The list is those indexes, newest first. A test pins that the top of the list equals `GetReceivingAddressAsync()` before and after generating.

**Alternatives**: Also list used addresses beyond `NextReceivingIndex` (a restore edge case). Not done: the wallet moves `NextReceivingIndex` past them on generate, and the spec defines the newest as the one on show. Noted as a check in tasks.

## R2. Deriving the addresses

**Finding**: `GetReceivingAddressesAsync(count, startIndex)` derives an ascending range of SegWit addresses. Derivation is elliptic-curve work in WASM, so it should not run for every index at once.

**Decision**: Use the existing call, newest 10 first, then 10 more per "Show more" (clarified). Keep what was derived for the visit so "Show more" never re-derives. No change to `WalletService`.

**Alternatives**: Derive all at page open (slow with many addresses); add a new `WalletService` method (not needed).

## R3. Used or unused without a new request (clarified)

**Finding**: Home stores the transaction list it loaded under `cache:transactions:<walletId>`. Each record now carries the wallet's own addresses (`WalletAddresses`, `WalletReceivedAddresses`, spec 010). `ActivityAddressIndex.Build` already turns records into per-address counts.

**Decision**: Read the cached history from storage and count per address with the existing helper. If there is no cache, load the history once in the background with `GetTransactionHistoryAsync` (the same call Home and Activity use, with its own 30-second cache); until it answers, and if it fails, the status is "unknown". A listed address that appears in the history is "used" with its count; one that does not is "unused" only when a history was actually loaded.

**Alternatives**: Ask the explorer about each address (extra requests; rejected in clarify). Treat missing history as "unused" (could mislead; rejected by FR-003).

**Known limit**: Older cached records written before spec 010 have no wallet addresses, so those addresses can show as unused until the history is refreshed. The background refresh also runs when the cache holds records without address data.

## R4. Names

**Finding**: Receive already edits one name with `AddressLabelService` (`SetLabelAsync`, `RemoveLabelAsync`, keyed by the address string as generated). The Activity picker writes the same store.

**Decision**: Load the whole label store once for the visit, show names in the list, and update it when a name is set in the list or in the existing box under the QR code. Both write through the same service, so Activity sees them with no extra step.

## R5. View activity

**Finding**: Activity restores a remembered address from `pref:activityAddress:<walletId>` after its first load, only if the address still appears in the history (spec 010).

**Decision**: "View activity" writes that preference for the chosen address and navigates to `/transactions`. No new route or query parameter. The action is hidden on unused and unknown addresses (clarified), which are exactly the addresses Activity would drop the preference for.

## R6. Switching and the wallet's current address

**Decision**: Switching only changes what the page shows (QR, copy, share, download, name box). It never writes `NextReceivingIndex`. The page opens on the newest address each time (clarified); nothing about the choice is stored.

## R7. Keeping the payment request

**Finding**: The amount and note are page state separate from the address.

**Decision**: Switching rebuilds the QR/payload with the new address and leaves the amount and note alone (spec scenario 4 of story 1).

## R8. Limit and save-failure messages

**Decision**: Unchanged. "Generate new address" still calls `GenerateNewReceivingAddressAsync`; its `InvalidOperationException` message still goes to the existing inline error, and the list is not changed on failure.

## R9. Folded row (clarified)

**Decision**: Render a "Your addresses (N)" row under the QR card, folded on every visit and opened by a tap; the open or closed state is page state only and is never saved. N is the number of generated addresses (`NextReceivingIndex + 1`). The row is a button with `aria-expanded`, so it works with a keyboard and a screen reader. Deriving the first 10 addresses can wait until the row is first opened, which keeps the page as fast as it is today for people who never open it.

**Alternatives**: Always open (pushes "Generate new address" and the share buttons down; rejected in clarify); open on desktop only (two layouts to test).

## R10. Privacy notice for a used address (clarified)

**Decision**: When the address on show has status `Used`, show a one-line notice under the QR code ("This address has been used before. A new address is better for privacy."). It is page text that depends only on the entry status already computed for the list, so it adds no request and never blocks copy, share or download. It is absent for `Unused` and `Unknown`. If the user opens Receive without ever opening the list, the status for the address on show is still needed: read it from the same cached history (R3) so the notice can appear even while the list is folded; it takes the status of the address on show from the helper's `StatusOf`, without deriving any list entry.

**Alternatives**: Confirm before sharing a used address (an extra tap for a legitimate use such as a fixed donation address; rejected in clarify); no notice (loses the privacy hint).

## R11. Unknown status label (clarified)

**Decision**: While no history is known, or the load failed, each entry shows a muted "Status unknown" label instead of a status; it is replaced when the history arrives. This is the page text for `ReceiveAddressStatus.Unknown`; the helper itself only returns the status.

## R12. Watch-only wallets

**Finding**: Watch-only (xpub) wallets are not treated as single-key wallets on Receive, and they have a public-key-only HD derivation (`IsWatchOnly`), so the same derivation calls work for them.

**Decision**: Show the list for them like any HD wallet. Task T028 confirms it in the preview, and notes what Receive does for a multisig wallet.
