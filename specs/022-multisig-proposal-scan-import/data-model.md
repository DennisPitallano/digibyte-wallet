# Data Model: Receive Multi-Sig Proposals

## PendingMultisigTransaction (existing — one field added)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| `ReceivedFromCoSigner` | bool | **new**, default `false` | True when the proposal was added by scanning/pasting/loading a co-signer's copy. Drives the "Received from a co-signer" label (FR-009). Old stored records read as `false`. |
| `UnsignedTxId` | string | existing | Identity of a proposal across all signature states (R2). |
| `Status` | `PendingTxStatus` | existing | `Cancelled` → restore returns to `AwaitingSignatures` or `ReadyToFinalize` by signature count. |
| `CancelledAt` | DateTime? | existing | Cleared on restore. |

No other stored fields change. "Coins already spent" is **derived at display time**, never stored.

## Receive result (new, in-memory)

`ProposalReceiveResult`

| Field | Type | Meaning |
|-------|------|---------|
| `Outcome` | `ProposalReceiveOutcome` | `Added`, `Merged`, `AlreadyUpToDate`, `Cancelled`, `Closed` |
| `Pending` | `PendingMultisigTransaction` | The new, updated, or existing local proposal |
| `NewSignatures` | int | Signatures gained (0 unless `Added`/`Merged`) |
| `RejectedSigners` | IReadOnlyList<string> | Public keys of co-signers whose signatures failed verification and were discarded. A signature from a key that is not a co-signer is never discarded: the whole PSBT is refused instead (FR-005, SC-005) |

## Wrong-wallet match (new, in-memory)

`ProposalWalletMatch` — `WalletId`, `Name`: a multi-sig of the same owner whose address the PSBT spends from.

## Hand-off (Web, scoped, in-memory, one-shot)

`ProposalHandoff` — `TargetWalletId`, `PsbtBase64`; `Take(walletId)` returns and clears it only when the id matches. Never persisted.

## State transitions

```text
(not on device) --receive--> AwaitingSignatures | ReadyToFinalize        [Added, ReceivedFromCoSigner=true]
AwaitingSignatures/ReadyToFinalize --receive(more sigs)--> same / ReadyToFinalize   [Merged]
AwaitingSignatures/ReadyToFinalize --receive(nothing new)--> unchanged   [AlreadyUpToDate]
Cancelled --receive--> unchanged, outcome Cancelled --user confirms Restore--> AwaitingSignatures | ReadyToFinalize
Broadcast --receive--> unchanged, outcome Closed
```

## Validation rules (from the spec)

- Must spend only this multi-sig's coins; one recipient; fee positive (reader problems → refusal) — FR-005, FR-011.
- Every signature must belong to a co-signer, otherwise the whole PSBT is refused; co-signer signatures that fail verification are dropped and reported — FR-005.
- Must be a PSBT (`ClassifyScan`) before any parsing — FR-008.
- Never creates a second entry for the same `UnsignedTxId` — FR-004, SC-003.
