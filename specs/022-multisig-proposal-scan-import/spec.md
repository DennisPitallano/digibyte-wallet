# Feature Specification: Receive Multi-Sig Proposals by QR Scan

**Feature Branch**: `022-multisig-proposal-scan-import`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "Help implement the QR scanning for pending transaction, right now we can generate the QR but there's no way other signer can scan it"

## Background

When a co-signer creates a payment proposal on a multi-sig wallet, the pending-transaction card shows a QR code (plus Copy / Save as file) so the other co-signers can sign. Today the only receiving path is "Import signature", which merges signatures into a proposal the device **already has**. A co-signer's own device does not have the proposal yet, so the QR cannot be scanned into anything. This feature closes that gap: a co-signer can scan (or paste / load) a shared proposal and have it appear in their own pending list, ready to review and sign.

## Clarifications

### Session 2026-11-18

- Q: When a scanned proposal spends coins that appear already spent elsewhere, refuse or add with a warning? → A: Add it, clearly marked "can't be signed – coins already spent", with signing disabled.
- Q: If the scanned proposal belongs to a different multi-sig wallet also on this device, refuse or offer to open the matching one? → A: Tell the user which wallet it belongs to and offer to open that wallet and add it there (no automatic add).
- Q: If a proposal cancelled on this device is scanned again, stay blocked or allow bringing it back? → A: Tell the user it was cancelled and offer "Restore this proposal", which re-adds it only after explicit confirmation.
- Q: After a successful scan adds a proposal, where does the user land? → A: Stay on the pending list, scroll to the new proposal, highlight its card (cards on this page are always expanded), and show a "Proposal added" confirmation.
- Q: Should signing a proposal received from someone else need an extra confirmation compared with your own? → A: No extra step, but the card is labelled "Received from a co-signer" and shows the full recipient address and amount before Sign is usable.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scan a proposal QR to add it to my pending list (Priority: P1)

Signer A creates a proposal and shows its QR code. Signer B, on another device with the same multi-sig wallet, opens the wallet's pending transactions, taps "Scan proposal", and points the camera at A's QR code. The proposal appears in B's pending list with recipient, amount, fee and signature progress, so B can review it and sign.

**Why this priority**: This is the missing half of the existing QR feature; without it the QR code is unusable by the people it is meant for.

**Independent Test**: With two devices holding the same multi-sig wallet, create a proposal on device A, scan its QR on device B, and confirm B shows the same proposal with matching recipient, amount and fee and can sign it.

**Acceptance Scenarios**:

1. **Given** B has the multi-sig wallet and does not have the proposal, **When** B scans A's proposal QR, **Then** the proposal is added to B's pending list showing recipient, amount, fee, and who has signed.
2. **Given** the proposal was added, **When** B reviews it, **Then** B sees the same "check the recipient and amount" prompt as for proposals B created, plus a "Received from a co-signer" label and the full recipient address and amount, before signing.
3. **Given** the scan succeeded, **When** the proposal is added, **Then** B stays on the pending list, which scrolls to the new proposal and highlights its card, with a "Proposal added" confirmation.

---

### User Story 2 - Scan a QR for a proposal I already have (Priority: P1)

B already has the proposal (from an earlier scan or a signing room). A QR of the proposal carrying additional signatures is scanned by B. Signatures are merged into the existing proposal rather than creating a duplicate.

**Why this priority**: Signatures travel the same QR route; a scan must do the right thing whether or not the proposal is already known.

**Independent Test**: Scan a proposal QR twice on the same device, and scan a QR carrying more signatures for an existing proposal; confirm there is one proposal and its signature count grows.

**Acceptance Scenarios**:

1. **Given** B already has the proposal, **When** B scans a QR of the same proposal, **Then** no duplicate is created and any new signatures are merged.
2. **Given** B has the proposal with 1 signature, **When** B scans a QR of it carrying 2 signatures, **Then** the proposal shows 2 signatures.
3. **Given** B scans the identical proposal again with nothing new, **Then** B is told it is already up to date.

---

### User Story 3 - Same options as the QR: paste and file (Priority: P2)

A proposal too large for a QR code can still be shared with Copy PSBT or Save as file. B can receive it by pasting or choosing a file from the same "Receive proposal" entry, with identical outcomes to scanning.

**Why this priority**: Larger proposals cannot be shown as QR; co-signers need an equivalent route so every proposal is receivable.

**Independent Test**: Copy a proposal on A, paste it on B; save it as a file on A, load the file on B; both add the proposal.

**Acceptance Scenarios**:

1. **Given** B has copied A's PSBT text, **When** B chooses paste, **Then** the proposal is added as if scanned.
2. **Given** B has A's saved proposal file, **When** B loads it, **Then** the proposal is added as if scanned.

---

### User Story 4 - Clear refusals for the wrong thing (Priority: P2)

B scans something unusable: a proposal for a different wallet, an address or unrelated code, a malformed proposal, or a closed (cancelled / already broadcast) proposal. B gets a plain-language reason and nothing is added.

**Why this priority**: Prevents confusion and protects funds by never adding proposals B cannot or should not act on.

**Independent Test**: Scan each of the above and confirm a specific message appears and the pending list is unchanged.

**Acceptance Scenarios**:

1. **Given** a proposal that spends from a different multi-sig wallet than the one open, **When** scanned, **Then** nothing is added to the open wallet; if it matches another wallet on this device B is told which one and offered to open it and add the proposal there, otherwise B is told it is for a wallet they don't have.
2. **Given** a QR holding an address, payment link or unrelated text, **When** scanned, **Then** B is told it is not a proposal.
3. **Given** a malformed or tampered proposal, **When** scanned, **Then** B is told it could not be read and nothing is added.
4. **Given** B previously cancelled this proposal, **When** it is scanned again, **Then** it is not silently re-added; B is told it was cancelled and offered "Restore this proposal", which re-adds it only after B confirms.

---

### Edge Cases

- Camera permission denied or no camera: the scanner explains this and offers paste / file instead.
- A proposal signed by a key that is not a co-signer of the wallet: refused with a reason.
- Watch-only copies of a wallet: may receive and review a proposal but cannot sign it (consistent with existing watch-only behaviour).
- A proposal too large to show as a single QR: no QR is shown (existing behaviour) and the card points to Copy / Save as file, which the receive entry accepts.
- A proposal whose coins are no longer spendable (already spent elsewhere): added, clearly marked "can't be signed – coins already spent", with signing disabled; it can still be cancelled/removed.
- Scan from the wallet page vs. the pending page: both entry points reach the same flow for the open wallet.

## Requirements *(mandatory)*

### Functional Requirements

*Terms*: the button that starts the flow is "Scan proposal"; the panel it opens (scan, paste, file) is "Receive proposal".

- **FR-001**: A co-signer MUST be able to start "receive proposal" from the wallet's pending-transactions screen, in addition to the existing per-proposal "Import signature".
- **FR-002**: The receive flow MUST accept a proposal by camera QR scan, pasted text, or a loaded file, with the same outcome for each.
- **FR-003**: When the proposal is not yet on the device, the system MUST add it to that wallet's pending list showing recipient, amount, fee and signature progress, without the user re-entering any details.
- **FR-004**: When the proposal is already on the device, the system MUST merge any new signatures into it and MUST NOT create a duplicate.
- **FR-005**: The system MUST verify the proposal belongs to the open multi-sig wallet and that every signature on it is from a co-signer of that wallet, and MUST refuse otherwise with a plain-language reason. A proposal carrying any signature from a non-co-signer MUST be refused in full, not partly accepted.
- **FR-006**: When a proposal belongs to another multi-sig wallet on the device, the system MUST offer to open that wallet and add it there, and MUST NOT add it automatically.
- **FR-007**: A proposal whose coins are already spent MUST be added but marked as unsignable, with signing disabled and cancel/remove still available.
- **FR-008**: The system MUST refuse non-proposal data (addresses, payment links, unrelated codes, malformed proposals) with a message that says what was wrong.
- **FR-009**: A received proposal MUST get the same review prompt and signing options (including online signing where available) as a proposal created on the device, and MUST additionally be labelled "Received from a co-signer" and show the full recipient address and amount before Sign is usable.
- **FR-010**: The system MUST NOT silently re-add a proposal the user cancelled on this device; it MUST tell the user it was cancelled and offer to restore it, re-adding only after explicit confirmation. It MUST NOT accept signatures onto closed proposals (cancelled or broadcast) without that restore.
- **FR-011**: The recipient, amount and fee shown for a received proposal MUST be derived from the proposal itself, not from any separately supplied text.
- **FR-012**: If the camera is unavailable or denied, the user MUST be offered paste or file as alternatives.
- **FR-013**: Received proposals MUST survive app restarts in the same way locally created proposals do.
- **FR-014**: Receiving a proposal MUST NOT require any server and MUST NOT send the proposal or any key material off the device.

### Key Entities

- **Pending proposal**: A proposed multi-sig payment with recipient, amount, fee, current signatures, and a status (awaiting signatures, ready, closed).
- **Shared proposal**: The portable form of a pending proposal (QR, text, or file) carrying everything another co-signer needs to review and sign it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A second co-signer can go from opening the pending screen to seeing the proposal ready to review in under 30 seconds using a QR scan.
- **SC-002**: 100% of proposals that can be shown as a QR code can be received by scanning that QR on another co-signer's device.
- **SC-003**: Scanning the same proposal any number of times never produces more than one entry in the pending list.
- **SC-004**: Every refused scan shows a reason that names the cause (wrong wallet, not a proposal, unreadable, closed, unknown signer); no refusal ends silently.
- **SC-005**: In testing, 0 proposals for a different wallet, or signed by non-co-signers, are added to a pending list.

## Assumptions

- Both co-signers already have the same multi-sig wallet (created or imported) on their own devices; adding someone to a wallet is out of scope.
- The existing QR display rules stay as they are: proposals above the single-QR size limit are not shown as QR and are shared by copy or file.
- Multi-part / animated QR for large proposals is out of scope for this feature.
- The existing camera scanner and paste / file helpers are reused; no new permissions model is introduced.
- Reviewing recipient and amount against what the payer actually asked for remains the user's responsibility, supported by the existing warning.
