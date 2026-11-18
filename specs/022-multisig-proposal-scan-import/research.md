# Research: Receive Multi-Sig Proposals by QR Scan

All Technical Context items were resolvable from the existing code; no open NEEDS CLARIFICATION remained.

## R1 — Reuse the online-signing adoption path

- **Decision**: Build `ReceiveProposalAsync` on `MultisigProposalReader.Read`, `AdoptProposalAsync` internals and `MergeRoomSignaturesAsync`.
- **Rationale**: They already take recipient/amount/fee from the PSBT (FR-011), reject inputs that are not this multi-sig's (FR-005; foreign signatures are handled per R1a below), verify each partial signature, and never trust display text. Writing a second parser would duplicate security-relevant code.
- **Alternatives**: Extend `ImportSignedPSBTAsync` (rejected: it requires an existing `PendingMultisigTransaction` and trusts the stored fields); call `AdoptProposalAsync` directly from the page (rejected: it throws for the cancelled case and returns no "added vs merged vs unchanged" outcome the spec needs).

## R1a — Foreign signatures are refused, not dropped

- **Decision**: `ReceiveProposalAsync` refuses the whole PSBT if any partial signature comes from a key that is not a co-signer. Only co-signer signatures that fail verification are dropped (reported as `RejectedSigners`).
- **Rationale**: FR-005 says refuse and SC-005 requires zero such proposals added; it matches `ImportSignedPSBTAsync`. Silently dropping would let a tampered copy in.
- **Alternatives**: Drop and continue, as `MergeRoomSignaturesAsync` does for room copies (rejected: contradicts the spec).

## R2 — Identity of "the same proposal"

- **Decision**: Match on the unsigned transaction id (`UnsignedTxId`), already stored per proposal.
- **Rationale**: It is identical for every signature state of the same payment, so rescans and signature-carrying copies resolve to one entry (FR-004, SC-003). Proposals saved before the field existed are backfilled on load (existing behaviour).
- **Alternatives**: Compare full PSBT text (rejected: changes with every signature).

## R3 — Outcome model

- **Decision**: `ReceiveProposalAsync` returns a result record: `Added`, `Merged` (new signatures), `AlreadyUpToDate`, `Cancelled` (carries the local pending item, nothing changed), `Closed` (already broadcast), plus the pending item. Unusable input throws `PsbtImportException` with a plain-language reason (existing convention).
- **Rationale**: The page needs to distinguish five outcomes (confirmation text, scroll target, restore offer) without re-querying; exceptions stay for refusals, matching the existing import API.
- **Alternatives**: Boolean + out params (rejected: unreadable at call sites).

## R4 — Restore a cancelled proposal

- **Decision**: `RestoreCancelledAsync(pending, psbtBase64)` re-validates the PSBT as in R1, clears `CancelledAt`, sets status to awaiting/ready from the merged signature count, and merges the supplied signatures. The page calls it only after the user taps "Restore this proposal".
- **Rationale**: Satisfies the clarified FR-010 (never silent). Cancellation is a local status, so restoring is a status change plus merge; no data was deleted unless the user had used "Remove cancelled".
- **Alternatives**: Allow `Adopt` to overwrite (rejected: silent).
- **Note**: If the cancelled proposal was already removed from the device it is simply a new proposal (Added).

## R5 — Wrong-wallet detection and hand-off

- **Decision**: `FindWalletForProposalAsync(psbt, ownerWalletId, ownPublicKeyHex)` checks each of the owner's multi-sigs: a PSBT belongs to a wallet when its inputs' scripts match that wallet's address. The page offers "Open *wallet name*" and stores the PSBT in a scoped, one-shot `ProposalHandoff`; the destination pending page consumes it on load and runs the normal receive flow. No automatic add.
- **Rationale**: Matches the clarified FR-006. Matching by input script uses data already inside the PSBT, with no extra wallet metadata. A scoped in-memory hand-off avoids putting a large PSBT in a URL (privacy rule: nothing sensitive in URLs).
- **Alternatives**: Redirect with the PSBT in the query string (rejected: size and URL privacy); auto-add to the match (rejected by clarification).

## R6 — Coins-already-spent marker

- **Decision**: Move the signing-room check ("are all inputs still unspent?") into the wallet service as `GetSpentInputIdsAsync` and use it from the pending page for open proposals; the room page keeps its behaviour via the same method. A proposal with a spent input shows "Can't be signed – coins already spent", with Sign/Online/Broadcast disabled and Cancel kept (FR-007).
- **Rationale**: One implementation for both pages. Applying the marker to all open proposals (not only received ones) is a strict superset of the spec requirement and avoids two code paths. On chain-lookup failure nothing is marked (constitution V).
- **Alternatives**: Refuse at receive time (rejected by clarification); check only at broadcast (rejected: dead-end proposals).

## R7 — "Received from a co-signer" label

- **Decision**: Persist `ReceivedFromCoSigner` on the pending item, set only by the new receive path; show a badge on the card. The card already shows the full recipient address and amount (`PaymentSummary`) and Sign already opens a confirmation, so the clarified FR-009 needs the label only.
- **Rationale**: A derived label (e.g. "not created by me") is unreliable because there is no stored creator. A boolean defaulting to false is backward-compatible.
- **Alternatives**: Infer from `CreatedAt`/signers (rejected: unreliable).

## R8 — After a successful add

- **Decision**: Stay on the pending list, refresh the queue, scroll the new card into view, keep it visually highlighted briefly, and show a "Proposal added" notice. Cards on this page are always expanded, so "expand" is satisfied by showing the card's full summary and signer progress.
- **Rationale**: Clarified UX; reuses the page's `message` notice and existing DOM helpers (`dom-helpers.js`).

## R9 — Inputs: scan, paste, file

- **Decision**: One `ReceiveProposalText(string)` handler fed by three sources: `QrScannerModal.OnScanned`, clipboard read, and `InputFile` (the same 512 KB `maxAllowedSize` and `.txt,.psbt,text/plain` filter as the import panel's `OnFileChosen`). `ClassifyScan` rejects addresses, payment links and unknown text before the service is called, with the same wording style as the import panel.
- **Rationale**: Identical outcome per source (FR-002); denied camera already falls back to the modal's close + paste/file in the panel (FR-012).
- **Alternatives**: A new scanner component (rejected: existing one works).

## R9a — QR readability at the size limit (risk)

- **Finding**: A proposal QR can hold about 2,200 characters (dense, error correction M), while `src/DigiByte.Web/wwwroot/js/qrscanner.js` requests only a 640×480 camera image and decodes with jsQR. A dense code may not decode reliably at that resolution.
- **Decision**: Verify on a real phone with a near-limit proposal before building the UI (task T008a). If it fails, apply in order: raise the requested camera resolution (e.g. ideal 1280×720), then lower the QR error correction to L and/or render the QR larger, then lower the QR size threshold so very large proposals fall back to Copy/Save as file.
- **Rationale**: SC-002 promises every QR-showable proposal can be scanned; this is the one part of the feature that depends on hardware and cannot be unit-tested.

## R10 — Out of scope (confirmed)

Multi-part/animated QR for proposals over the single-QR limit; adding co-signers to a wallet; changing room behaviour; server involvement.
