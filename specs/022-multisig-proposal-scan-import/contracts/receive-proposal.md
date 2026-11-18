# Contract: Receive Proposal

## Service (DigiByte.Wallet — `MultisigWalletService`)

```text
Task<ProposalReceiveResult> ReceiveProposalAsync(MultisigWalletConfig config, string psbtBase64)
```
- Reads facts with `MultisigProposalReader`; any problem → `PsbtImportException(reason)`; nothing saved.
- Before saving or merging, any partial signature from a key that is not a co-signer of this multi-sig → `PsbtImportException`; nothing saved or changed.
- No local copy → saves it (signatures stripped, then each verified signature merged), sets `ReceivedFromCoSigner = true`, returns `Added`.
- Local copy, open → merges verified signatures; returns `Merged` if any new, else `AlreadyUpToDate`.
- Local copy `Cancelled` → no change, returns `Cancelled` with the local item.
- Local copy `Broadcast` → no change, returns `Closed`.
- Stored recipient/amount/fee must match the PSBT (reuses `CheckProposalMatches`); mismatch → refusal.

```text
Task<PendingMultisigTransaction> RestoreCancelledAsync(PendingMultisigTransaction pending, string psbtBase64)
```
- Only for `Cancelled` items; re-validates and merges as above, clears `CancelledAt`, sets status from signature count.

```text
Task<ProposalWalletMatch?> FindWalletForProposalAsync(string psbtBase64, string ownerWalletId, string? ownPublicKeyHex, string excludeWalletId)
```
- First other wallet of this owner whose address the PSBT's inputs spend from; null if none.

```text
Task<IReadOnlySet<string>> GetSpentInputIdsAsync(MultisigWalletConfig config, IEnumerable<OutPoint> inputs)
```
- Inputs (as `txid:index`) not in the wallet's current spendable set. Empty set if chain data is unavailable.

## Refusal messages (plain language, one per cause — SC-004)

| Cause | Message intent |
|-------|----------------|
| Not a PSBT (address, payment link, other) | "That is an address / not a proposal…" |
| Unreadable / tampered | "That proposal couldn't be read." |
| Spends other coins | "This payment spends coins that are not this multisig's." (+ offer to open the matching wallet if found, else "…a wallet you don't have") |
| Foreign signer (any signature from a key that is not a co-signer) | Whole PSBT refused, nothing added: "This copy is signed by a key that is not a co-signer of this multisig." |
| Co-signer signature that fails verification | Signature discarded and reported in `RejectedSigners`; the rest of the proposal is still used |
| Different network | Existing network-specific reader message |

## UI contract (MultisigPending page)

- **Entry**: a "Scan proposal" button above the list opens the scanner (also reachable via `?scan=1` from the wallet page). A "Receive proposal" panel offers Scan QR, Paste, and Choose file; all call one handler.
- **Added**: notice "Proposal added", queue reloads, card scrolled into view and highlighted; card shows a "Received from a co-signer" badge.
- **Merged**: notice "N new signature(s) added". **AlreadyUpToDate**: notice "Already up to date."
- **Cancelled**: inline prompt "You cancelled this proposal." with **Restore this proposal** / **Leave it cancelled**.
- **Wrong wallet**: prompt "This proposal is for *<name>*." with **Open *<name>*** (hands off via `ProposalHandoff`); otherwise a refusal.
- **Spent coins**: badge "Can't be signed – coins already spent"; Sign, Sign together online and Broadcast disabled; Cancel/Share still available.
- **Camera denied/unavailable**: the scanner closes with its message; Paste and Choose file remain on the panel.
- All controls ≥ 44 px, labelled for screen readers; outcomes announced via the page's existing `role="status"` notice.
