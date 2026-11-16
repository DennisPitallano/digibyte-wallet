# Research: Multisig Management Revamp

All Technical Context items resolved from the existing codebase; no external research was needed.

## R1 — Wallet scoping

- **Finding**: `MultisigWalletService` stores one global `multisig_wallet_list` and one global `multisig_drafts` list. Configs carry `WalletId` (the multisig's own id) but no reference to the owning wallet, so every wallet sees every multisig.
- **Decision**: Add `OwnerWalletIds` (a list; the active app wallet's id from `WalletService.GetActiveWalletIdAsync()` is added on create/import) to list entries and configs, and a single `OwnerWalletId` to drafts. Pending items inherit scoping through their multisig. Listing/getting accepts the active wallet id and filters. Keep the storage keys; add fields additively.
- **Rationale**: Smallest change that fixes the defect and keeps old data readable. Pending items are already keyed by multisig id, so they inherit scoping through their multisig.
- **Alternatives**: Per-wallet storage key prefixes (breaks existing data, needs a bulk move); filtering only in the UI (leaks via direct routes and is untestable).

## R2 — Legacy migration and claiming

- **Decision**: A one-time, idempotent migration at list time. For each entry not yet owned by the active wallet, compare the active wallet's public key with the config's co-signer keys; on a match add the wallet id to `OwnerWalletIds` (so two wallets that both hold keys each see the multisig). Unmatched entries stay ownerless, are excluded from every wallet's list, and are surfaced as "Unassigned multisigs" with a Claim action that sets the owner to the active wallet (per spec clarification).
- **Rationale**: Public keys are only derivable for the unlocked wallet (`WalletService.GetPublicKey()`), so matching happens lazily for the active wallet; anything it cannot match goes to the claim prompt. Nothing is deleted. The same attribution runs inside the access check used when a multisig is opened directly by link, so a user's own legacy multisig is never shown as "another wallet".
- **Alternatives**: Assign to whichever wallet is active first (rejected in clarification); keep visible everywhere (the bug).

## R3 — Rename and labels

- **Decision**: Name lives on the list entry (already stored there) and is exposed on the config view model; add `RenameWalletAsync` / `RenameDraftAsync` and `RenameCoSignerAsync`. `CoSigner.Name` is already mutable. Trim, reject empty, cap 40 chars; duplicate names allowed with a warning flag returned to the UI. Names stay local; backup may include them as a note only (clarification Q2).

## R4 — Full public keys

- **Decision**: A `CoSignerRow` component that expands in place to show the 66-character compressed key grouped in blocks (e.g. 8×8 with a final block), Copy, and a QR. Reuse the project's existing QR rendering approach used by Receive.
- **Rationale**: Pure presentation; no data change. Keys are never editable.

## R5 — Dashboard layout and backup status

- **Decision**: Header (name + badge) → balance → address row (copy/QR) → three primary actions → collapsible Co-signers, Redeem script & backup, Recent activity. Add `BackupAt` (UTC) to the config; set it when the user downloads the backup or copies the script; show the full amber prompt until set, then a one-line "Backed up <date>" status (clarification Q3).

## R6 — Send flow (scan/paste/Max/review)

- **Findings**: `QrScannerModal` (`OnScanned`), `PaymentRequest.FromUri`, `AddressValidator.IsValid`, and `IBlockchainService.GetFeeRateAsync` exist. Current service estimates fee as `feeRate.GetFee(250)` regardless of inputs.
- **Decision**: Add a pure helper in `DigiByte.Wallet` that, given UTXOs, a fee rate, and a destination count, returns estimated vsize/fee, max sendable, and remaining balance. Max = total − fee. Validation order: address (network-aware), dust, amount ≤ spendable. The wallet has no existing dust constant (dust prevention is disabled in the builders), so `MultisigSendHelper.DustThresholdSatoshis` is set to 100,000 sat (0.001 DGB), a conservative floor; verify against node relay policy during implementation. Fee rate uses the regular Send page's sat/vB model and default (the old 5 sat/byte default in `CreateSpendingPSBTAsync` is replaced), with size estimated from input count and script size. Review step before `CreateSpendingPSBTAsync`; double-submit guarded in the page. After creation, offer Sign now and Share. Known recipients come from the existing Contacts store when available.
- **Rationale**: Fixes a real accuracy gap (flat 250-byte estimate) and makes Max correct.
- **Alternatives**: Keep the flat estimate (Max would fail on large multi-input spends).

## R6a — Scanner content routing

- **Decision**: Scanned text is classified as: DigiByte address, `digibyte:` URI, PSBT (base64, starts with `cHNidP`), or unknown. The Send page accepts the first two and rejects the others with an explanation; the Pending import accepts PSBT.

## R7 — Activity / history

- **Finding**: `GetAddressTransactionsAsync(address, skip, take)` supports paging, and each `TransactionInfo` includes inputs/outputs with addresses, fee, confirmations, and timestamp.
- **Decision**: New pure `MultisigActivityBuilder` that merges (a) chain transactions for the multisig address, classified as Received (outputs to the address, inputs not from it) or Sent (inputs from the address), and (b) local proposals. Dedupe by TxID so a broadcast proposal appears once as Sent. Page first requests `take: 25`; "Load more" requests the next page (clarification Q5). Filters: All/Received/Sent/Pending/Failed; group by calendar date. Offline: show local proposals/previous sends with a notice.
- **Rationale**: Pure function is unit-testable; no new endpoint or backend.
- **Alternatives**: Persist a local ledger (stale and double bookkeeping); load all history (slow, rejected in clarification).

## R7a — Sent amount classification

- **Decision**: For a Sent entry, amount = sum of outputs not to the multisig address (change excluded); counterparty = first such output address (with "+N more" when several).

## R8 — Pending queue and import verification

- **Findings**: `ImportSignedPSBTAsync` combines any PSBT without checking that it matches the proposal; `SignedBy` is only updated from partial signatures.
- **Decision**: Backfill `UnsignedTxId` for older proposals from their stored PSBT on load, then before combining verify the imported PSBT has the same unsigned transaction id as the pending item and that signatures come from keys in the redeem script; otherwise reject with a specific reason. Add `Cancelled` handling (status already exists) with a service method that never allows broadcast of a cancelled item; confirmation copy states cancel is local only (clarification Q4). Sort order: needs-my-signature, ready-to-broadcast, awaiting others, failed, cancelled. Retry broadcast leaves status recoverable.
- **Rationale**: Closes a correctness/security gap (foreign or tampered PSBT) required by FR-023.

## R9 — Persistence compatibility

- **Decision**: All new JSON fields are nullable or defaulted so existing stored data deserializes unchanged; migration is idempotent and never deletes. Wallet deletion cleanup removes the owned multisigs' pending data only after the existing backup warning.

## R10 — Testing approach

- **Decision**: Test-first in `DigiByte.Wallet.Tests` using the existing in-memory storage/blockchain test doubles used by `MultisigWalletServiceTests`. UI is verified manually via `quickstart.md` on 360×800 and 320 px, light and dark.
