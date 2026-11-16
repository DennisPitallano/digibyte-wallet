# Contracts: Multisig Management Revamp

This feature exposes no network API. The contracts are (1) the `MultisigWalletService` surface used by the pages and (2) the page/route and component contracts.

## 1. MultisigWalletService (additions/changes)

All methods that read multisigs take the active wallet id. Existing callers must pass it.

| Method | Behaviour |
|---|---|
| `ListWalletsAsync(ownerWalletId, ownPublicKeyHex)` | Returns only entries whose `OwnerWalletIds` contain that wallet (after lazy ownership migration by co-signer key). Never returns ownerless or other-owner entries |
| `ListUnassignedAsync()` | Entries with no owner (candidates for claim) |
| `ClaimAsync(walletId, ownerWalletId)` | Adds the owner to an unassigned entry; idempotent; errors if the entry already has a different owner |
| `GetAccessStateAsync(walletId, ownerWalletId, ownPublicKeyHex)` | Returns `Owned`, `OtherWallet`, or `NotFound`. Runs the same key-based attribution as the list before deciding, so a direct link to the user's own legacy multisig works even if it was never listed |
| `GetConfigAsync(walletId, ownerWalletId)` | Returns the config only when access state is `Owned`; otherwise null |
| `ListDraftsAsync(ownerWalletId, ownPublicKeyHex)` / `SaveDraftAsync` | Drafts carry a single owner and filter by it; legacy drafts are matched on `OwnPublicKeyHex` |
| `CreateMultisigWalletAsync(..., ownerWalletId)` / `ImportMultisigWalletAsync(..., ownerWalletId)` | Persist with owner |
| `RenameWalletAsync(walletId, name)` → `RenameResult { Ok, Error?, DuplicateWarning }` | Trim; 1–40 chars, longer names rejected (never capped); no script change |
| `RenameDraftAsync(draftId, name)` | Same rules |
| `RenameCoSignerAsync(walletId, publicKeyHex, name)` | Label only; key immutable |
| `MarkBackedUpAsync(walletId)` | Sets `BackupAt = UtcNow` |
| `CreateSpendingPSBTAsync(..., submissionToken)` | Idempotent per token: a repeated call with the same token returns the existing proposal |
| `MultisigSendHelper.EstimateSend(utxos, feeRateSatPerVByte, amount, outputCount)` → `{ Fee, Max, Remaining, Error? }` | Pure static helper; fee = estimated vsize (from input count and script size) × rate; change below the dust threshold is folded into the fee; used by Send for Max and live remaining balance |
| `MultisigSendHelper.DustThresholdSatoshis` | Constant 100,000 (0.001 DGB); outputs below it are rejected |
| `MultisigSendHelper.ValidateDestination(address, network)` → `{ Ok, Error }` | Address checksum/network check; flags destination = own multisig address as a warning |
| `MultisigSendHelper.ClassifyScan(payload)` | Classifies scanned text per section 4 |
| `ImportSignedPSBTAsync(pending, psbt)` | NOW verifies same unsigned txid and signer keys ∈ redeem script; throws a typed error with a user-facing reason otherwise |
| `CancelPendingAsync(pendingId)` | Sets Cancelled; `FinalizeAndBroadcastAsync` refuses cancelled items |
| `RetryBroadcastAsync(pending)` | Recoverable path from Failed. `FinalizeAndBroadcastAsync` and retry are guarded so concurrent calls broadcast once |
| `GetPendingTransactionsAsync(walletId)` | Backfills `UnsignedTxId` for proposals saved before this feature |
| `GetActivityAsync(config, skip, take)` | Fetches chain page + proposals, returns `ActivityEntry` list via `MultisigActivityBuilder`, with an `IsPartial/Offline` flag |
| `DeleteWalletAsync(walletId, ownerWalletId)` | Removes only that wallet from `OwnerWalletIds`; when no owner remains, also removes the config, pending items, and list entry. The UI confirms first with the redeem-script backup warning |

`MultisigActivityBuilder.Build(address, chainTxs, proposals)` is pure and deterministic (no storage, no network).

## 2. Routes

| Route | Purpose |
|---|---|
| `/multisig` | Scoped list, drafts, unassigned prompt, rename |
| `/multisig/{id}` | Dashboard; shows "belongs to another wallet" state if the owner differs |
| `/multisig/{id}/send` | Guided send (paste/scan/Max/review) |
| `/multisig/{id}/pending` | Pending queue |
| `/multisig/{id}/activity` | NEW. Unified Activity with filters and Load more |
| `/multisig/{id}/activity/{txOrProposalId}` | NEW. Entry detail (inline panel acceptable) |

## 3. Component contracts (`Components/Multisig/`)

| Component | Inputs | Outputs |
|---|---|---|
| `MultisigHeader` | Name, M/N, watch-only flag | `OnRename` |
| `ReceiveSheet` | Address | Address, QR, Copy |
| `CoSignerRow` | CoSigner, IsOwn, expanded state | `OnRenameLabel`, copy key |
| `KeyView` | Public key hex | Grouped display, copy, QR |
| `BackupCard` | `BackupAt`, redeem script | `OnBackupDone` (download/copy) |
| `ActivityList` / `ActivityItem` | Entries, filter | `OnSelect`, `OnLoadMore` |
| `SignerProgress` | Co-signers, SignedBy, M | Read-only checklist |
| `RenameDialog` | Current name | `OnSave(name)` |

## 4. Scanner payload classification

| Payload | Send page | Pending import |
|---|---|---|
| DigiByte address | Accept | Reject (explain) |
| `digibyte:` URI (address, amount, label) | Accept, prefill | Reject |
| PSBT (base64, `cHNidP…`) | Reject (explain) | Accept |
| Anything else | Reject (explain) | Reject |

## 5. Accessibility / layout contract

Icon-only buttons have accessible labels; touch targets ≥ 44 px; no horizontal scroll at 320 px; both themes; long values (addresses, keys, TxIDs) wrap or expand, never clip without a way to view the full value.
