# Data Model: Multisig Management Revamp

All additions are backward compatible: new fields are nullable or defaulted, so previously stored JSON still deserializes.

## MultisigWalletEntry (list entry, stored in `multisig_wallet_list`)

| Field | Type | Notes |
|---|---|---|
| Id | string | Multisig id (existing) |
| Name | string | Display name; editable; trimmed, 1–40 chars |
| OwnerWalletIds | List&lt;string&gt; | NEW. App wallets that own it (creator/importer, or holders of a co-signer key); empty = legacy/unassigned |

## MultisigWalletConfig (stored in `multisig_config_{id}`)

Existing fields unchanged (address, redeem script, threshold, co-signers, OwnKeyIndex, CreatedAt). Added:

| Field | Type | Notes |
|---|---|---|
| OwnerWalletIds | List&lt;string&gt; | NEW. Mirrors the entry; the entry is the source of truth for filtering |
| Name | string? | NEW. Display name copy for backup/headers; derived from the entry |
| BackupAt | DateTime? | NEW. Set when the user downloads the backup or copies the redeem script |

Rules: address, redeem script, threshold, and keys are immutable. `CoSigner.Name` is editable (non-empty, ≤ 40 chars); `PublicKeyHex` is not. A name never feeds script construction. Name and labels are shared by every owner wallet. Removing a multisig removes only the active wallet from `OwnerWalletIds`; the config, pending items, and list entry are deleted when the last owner is removed.

## MultisigDraft

| Field | Type | Notes |
|---|---|---|
| Name | string | Existing; now editable from the list |
| OwnerWalletId | string? | NEW. Drafts belong to the single wallet that started them; legacy drafts match on `OwnPublicKeyHex` |

## PendingMultisigTransaction

Existing fields kept. Added/clarified:

| Field | Type | Notes |
|---|---|---|
| UnsignedTxId | string | NEW. Id of the unsigned transaction inside the PSBT; used to verify imported signatures belong to this proposal. Proposals saved before this feature have it empty; it is computed from the stored PSBT when the proposal is loaded and saved back |
| Memo | string? | Surfaced in Pending and Activity |
| FailureReason | string? | NEW. Human-readable last broadcast error |
| CancelledAt | DateTime? | NEW |
| UpdatedAt | DateTime | NEW |

State transitions:

```text
AwaitingSignatures → ReadyToFinalize  (signature count ≥ M)
ReadyToFinalize    → Broadcast        (broadcast succeeds; BroadcastTxId set)
ReadyToFinalize    → Failed           (broadcast error; FailureReason set; retry returns to ReadyToFinalize)
AwaitingSignatures | ReadyToFinalize | Failed → Cancelled   (local only; never broadcast from this device afterwards)
```

`Broadcast` and `Cancelled` are terminal for this device.

## ActivityEntry (computed, not stored)

| Field | Type | Notes |
|---|---|---|
| Kind | enum | Received, Sent, ProposalAwaiting, ProposalReady, Failed, Cancelled |
| TxId | string? | Null for unbroadcast proposals |
| Direction | enum | In, Out, None |
| AmountSatoshis | long | Received: paid to the multisig address. Sent: outputs not to the multisig address (change excluded) |
| FeeSatoshis | long? | |
| Counterparty | string? | First external output address (Sent) or first external input address (Received); `MoreCount` for others |
| Memo | string? | From the matching proposal |
| Timestamp | DateTime | Chain time, else proposal `UpdatedAt` |
| Confirmations | int? | Null for unbroadcast |
| ProposalId | string? | Link to the proposal when one exists |
| SignedCount / RequiredCount | int? | For proposal entries |

Rules: a proposal whose `BroadcastTxId` matches a chain transaction yields a single Sent entry (no duplicate). Sort newest first; group by local calendar date. Filters: All, Received, Sent, Pending (awaiting+ready), Failed (failed+cancelled).

## Ownership resolution (legacy migration)

For an entry whose `OwnerWalletIds` does not contain the active wallet id: if the active wallet's public key equals any co-signer key in the config, add the active wallet id to `OwnerWalletIds` (so two wallets that both hold keys each see it). An entry whose `OwnerWalletIds` stays empty is hidden from every wallet's list and offered via "Unassigned multisigs → Claim", which adds the active wallet id as its owner. The same single-owner rule applies to drafts using `OwnPublicKeyHex`. Opening a multisig directly (for example from a saved link) runs the same attribution before deciding between Owned, OtherWallet, and NotFound.
