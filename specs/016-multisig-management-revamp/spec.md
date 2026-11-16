# Feature Specification: Multisig Management Revamp

**Feature Branch**: `feature/multisig-management-revamp`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "Revamp and enhance multisig management. Issues: (1) a multisig created from the active wallet is also visible after switching to another wallet; (2) the multisig name cannot be edited; (3) the full co-signer public keys cannot be seen on the multisig dashboard. Enhancements: modern, compact dashboard with well-arranged data and calls to action; transaction activity; improved create-transaction page (scan / paste etc.); improved pending transactions; improved transaction history."

## Clarifications

### Session 2026-11-17

- Q: What happens to a pre-existing multisig that can't be matched to a wallet by co-signer keys? → A: It is hidden from all wallets, and each wallet shows an "Unassigned multisigs" prompt (whenever unassigned items exist, dismissible for the current session) where the user can claim it for that wallet.
- Q: Do the multisig name and co-signer labels travel with the multisig when shared? → A: No. They stay local to each device; the backup file may include them as an informational note, but importing never overwrites existing labels.
- Q: How is the redeem script considered "backed up"? → A: Once the user downloads the backup file or copies the redeem script; the status shows the date, and the prompt stays until one of those happens.
- Q: Does cancelling a proposal stop co-signers who already hold a copy? → A: No. Cancel is local only; the confirmation tells the user other copies could still be signed and broadcast, and a cancelled proposal is never broadcast from this device.
- Q: How much history does Activity load from the chain? → A: The latest 25 entries first, then older ones on demand ("Load more") until the full history is shown.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Multisig wallets belong to the wallet that created them (Priority: P1)

A person who keeps several wallets on the same device creates a multisig from Wallet A. When they switch to Wallet B, the Multisig Wallets list (and its drafts) must not show Wallet A's multisig. Switching back to Wallet A shows it again. Today the multisig appears under every wallet, which is confusing and exposes one wallet's co-signer setup, balance, and pending payments inside an unrelated wallet.

**Why this priority**: It is a correctness and privacy defect. Showing another wallet's multisig inside the wrong wallet can lead to signing or sending from the wrong context and undermines trust in the wallet switcher. It must be fixed before any visual polish.

**Independent Test**: Create a multisig (and a draft) under Wallet A, switch to Wallet B, open Multisig Wallets and confirm neither appears; switch back to Wallet A and confirm both are present with unchanged data.

**Acceptance Scenarios**:

1. **Given** Wallet A owns a multisig and a draft, **When** the user switches to Wallet B and opens Multisig Wallets, **Then** the list shows no multisig or draft from Wallet A and shows the empty state with Create / Import actions.
2. **Given** the user is on Wallet B, **When** they create or import a multisig, **Then** it appears only under Wallet B and not under Wallet A.
3. **Given** a multisig was created before this change (no recorded owner), **When** the user opens Multisig Wallets after updating, **Then** it is attributed to the wallet whose key is one of its co-signers; if no wallet matches, it is hidden everywhere and offered through an "Unassigned multisigs" prompt that lets the user claim it for the current wallet. It is never silently shown under unrelated wallets.
4. **Given** the user deletes a multisig, **When** they confirm, **Then** they are first warned about the redeem-script backup, and after deletion it is removed from this wallet; if no other wallet on the device owns it, its pending proposals are removed too.
5. **Given** a direct link or bookmark to Wallet A's multisig is opened while Wallet B is active, **When** the page loads, **Then** the user sees a clear "belongs to another wallet" message with a way back, not the multisig's contents.

---

### User Story 2 - Rename a multisig (Priority: P1)

A user can change the display name of a multisig wallet (and of its draft) at any time, from the dashboard and from the list. The name is a local label only; renaming never changes the address, redeem script, or co-signer keys.

**Why this priority**: Users cannot fix typos or give meaningful names (e.g. "Family Treasury"), and the default "2-of-3 Multisig" name is indistinguishable when several exist.

**Independent Test**: Open a multisig, rename it, return to the list and restart the app; the new name shows everywhere (list, dashboard header, activity entries, backup file) and the address is unchanged.

**Acceptance Scenarios**:

1. **Given** a multisig dashboard, **When** the user taps the name (or an edit control), edits it, and confirms, **Then** the new name appears immediately in the header and persists across restarts.
2. **Given** an empty or whitespace-only name, **When** the user confirms, **Then** the change is rejected with a clear message and the previous name is kept.
3. **Given** a name that duplicates another multisig under the same wallet, **When** the user confirms, **Then** it is allowed but the user is gently warned that two entries share a name.
4. **Given** a very long name, **When** it is saved, **Then** names longer than 40 characters are rejected with a clear message (never silently cut), and saved names display truncated gracefully in compact layouts.
5. **Given** a draft in the list, **When** the user renames it, **Then** the draft keeps all other progress.

---

### User Story 3 - See and verify full co-signer public keys (Priority: P1)

On the multisig dashboard each co-signer row currently shows only a shortened key (e.g. "022ea0d6f36ade72…"). The user must be able to view the full compressed public key of every co-signer, copy it, and compare it with what a co-signer sent them, without leaving the dashboard.

**Why this priority**: Verifying keys is the core security step of multisig setup; a truncated key cannot be checked for substitution or typos.

**Independent Test**: Open a multisig with three co-signers, expand each co-signer, confirm the entire 66-character key is shown, copy works, and the "You" row is marked.

**Acceptance Scenarios**:

1. **Given** the co-signers list, **When** the user taps a co-signer row, **Then** the row expands to show the full public key (wrapped, never clipped), a Copy action, and whether it is this device's key.
2. **Given** an expanded key, **When** the user taps Copy, **Then** the full key is on the clipboard and a short confirmation is shown.
3. **Given** a co-signer, **When** the user edits that co-signer's label, **Then** the label updates everywhere and the key is unchanged (keys themselves are never editable).
4. **Given** a co-signer key, **When** the user wants to compare it, **Then** an easy-to-read grouped format (blocks of 4–8 characters) is offered alongside the raw copy value, and a QR code for the key is available.
5. **Given** a small phone screen, **When** the keys are shown, **Then** no text is cut off and no horizontal scrolling is needed.

---

### User Story 4 - Modern, compact multisig dashboard (Priority: P2)

The multisig dashboard is reorganised so the most important facts and actions fit on one screen without scrolling on a typical phone: a balance header (with the M-of-N badge), prominent primary actions (Send, Receive/Copy address, Pending), then compact collapsible sections for Co-signers, Redeem script & backup, and Recent activity. The long, always-expanded redeem script block and the large backup warning are condensed (e.g. a one-line backup status with expandable detail) while still making clear that the redeem script must be backed up.

**Why this priority**: Improves everyday usability and discoverability but depends on the P1 fixes (naming, full keys) to be meaningful.

**Independent Test**: Open the dashboard on a 360×800 viewport and confirm balance, address, and the three primary actions are visible without scrolling; expanding each secondary section works and the backup requirement is still evident.

**Acceptance Scenarios**:

1. **Given** the dashboard loads, **When** the user views the top of the page, **Then** they see the editable name, M-of-N badge, balance, address with copy and QR, and the primary actions Send, Receive, and Pending (with a count badge when items need attention).
2. **Given** pending transactions exist that need this user's signature, **When** the dashboard loads, **Then** a prominent "Needs your signature" call to action is shown.
3. **Given** a backup has not been downloaded or copied since creation, **When** the user views the dashboard, **Then** a compact, persistent "Back up redeem script" prompt is visible; once backed up it collapses to a quiet status.
4. **Given** the redeem script section, **When** the user expands it, **Then** the full script is shown with Copy and Download backup actions; collapsed, only a short summary is shown.
5. **Given** the balance is loading or the network is unavailable, **When** the dashboard renders, **Then** skeleton/offline states are shown rather than a misleading 0.
6. **Given** dark and light themes and small/large text sizes, **When** the dashboard is shown, **Then** all content remains legible and tappable (touch targets at least 44 px).

---

### User Story 5 - Improved create-transaction page with scan / paste (Priority: P2)

The user starting a payment from a multisig can fill the destination by pasting, scanning a QR code, or picking a known recipient; amount entry supports "Max" and shows available balance, estimated fee, and resulting remaining balance; invalid addresses and over-balance amounts are flagged inline before submitting. A review step summarises recipient, amount, fee, and the number of signatures required before the proposal is created.

**Why this priority**: Sending is the main purpose of a multisig; reducing address-entry mistakes and showing fees/remaining balance prevents costly errors.

**Independent Test**: From the dashboard, tap Send; paste an address, scan a QR (including a `digibyte:` payment URI that carries an amount), tap Max, review, and create the proposal; invalid and insufficient-balance inputs are blocked with clear messages.

**Acceptance Scenarios**:

1. **Given** the Send page, **When** the user taps Paste, **Then** the clipboard address is inserted and validated for the current network.
2. **Given** the Send page, **When** the user taps Scan and scans a DigiByte address or payment URI, **Then** the destination (and amount/label if present) is prefilled.
3. **Given** an address for the wrong network or with a bad checksum, **When** it is entered, **Then** an inline error appears and the Continue action is disabled.
4. **Given** the user taps Max, **When** the fee is estimated, **Then** the amount equals balance minus fee and the remaining balance shows zero.
5. **Given** an amount greater than the spendable balance, **When** the user continues, **Then** the proposal is blocked with a message naming the available balance.
6. **Given** a valid form, **When** the user continues, **Then** a review screen shows recipient (full address), amount, fee, signatures required, and memo, with Confirm and Edit; confirming creates a pending transaction and offers to sign it immediately and share it with co-signers.
7. **Given** the destination equals the multisig's own address, **When** entered, **Then** the user is warned.

---

### User Story 6 - Transaction activity and history (Priority: P2)

The multisig has a unified Activity view listing everything that happened to it, newest first: payments received, payments sent, and proposals awaiting signatures, ready to broadcast, failed, or cancelled. Each entry shows direction, amount, counterparty (shortened, full value on tap), memo, date, status, and confirmations. Tapping an entry opens a detail view with the full transaction ID (copy and open in explorer), full addresses, fee, signer progress, and timeline. The dashboard shows the latest few entries with a "View all" link. History supports filtering (All / Received / Sent / Pending / Failed) and grouping by date.

**Why this priority**: Users currently cannot see what a multisig has done; received funds and completed sends are invisible after broadcast.

**Independent Test**: Receive funds to the multisig, create/sign/broadcast a payment, and confirm both appear in Activity with correct status; filter by Sent and by Received; open a detail view and copy the transaction ID.

**Acceptance Scenarios**:

1. **Given** funds were received at the multisig address, **When** the user opens Activity, **Then** a "Received" entry appears with amount, date, and confirmation state.
2. **Given** a proposal was broadcast, **When** viewing Activity, **Then** it appears as "Sent" with its TxID and confirmation count, replacing its pending entry rather than duplicating it.
3. **Given** many entries, **When** the user applies a filter, **Then** only matching entries show and the empty state for that filter is meaningful.
4. **Given** no activity yet, **When** Activity opens, **Then** an empty state explains how to receive or send and offers those actions.
5. **Given** a long history, **When** scrolling, **Then** the first 25 entries show quickly and older ones load on demand via "Load more", without freezing the UI.
6. **Given** the user is offline, **When** Activity opens, **Then** locally known entries (proposals, previously broadcast sends) are shown with a notice that chain data is unavailable.

---

### User Story 7 - Improved pending transactions (Priority: P3)

The pending list becomes a clear to-do queue: each item shows recipient, amount, fee, memo, created time, and a signer checklist ("You ✓ · Treasury ✓ · Admin —") with a progress indicator (e.g. 1 of 2). Actions are contextual: Sign (if this device holds an unsigned required key), Share/Export (copy PSBT, QR, or file), Import signature from a co-signer (paste, scan, or file), Broadcast (when threshold is met), and Cancel/Delete. Items needing the user's action come first, and expired/failed items are distinguishable.

**Why this priority**: Builds on the activity model; valuable, but the multisig is usable without it.

**Independent Test**: Create a 2-of-3 proposal, sign locally, share it, import a co-signer's signed copy via paste, and broadcast; each step updates the progress indicator and the signer checklist.

**Acceptance Scenarios**:

1. **Given** a pending proposal this device has not signed, **When** the list is shown, **Then** it appears at the top with a "Sign" action and a "Needs your signature" label.
2. **Given** a proposal with enough signatures, **When** viewing it, **Then** "Broadcast" is the primary action and signer progress shows complete.
3. **Given** a co-signer returns a signed copy, **When** the user pastes or scans it, **Then** only valid signatures for that proposal and wallet are merged and the checklist updates; unrelated or tampered data is rejected with a clear reason.
4. **Given** a proposal the user no longer wants, **When** they cancel it (after confirmation), **Then** the confirmation explains the cancel is local only (co-signers with a copy could still broadcast it), and the proposal moves to history as Cancelled and can no longer be broadcast from this device.
5. **Given** a broadcast fails, **When** the failure is shown, **Then** the reason is human-readable and the proposal stays recoverable (retry).

---

### Edge Cases

- Switching wallets while a multisig page is open: the page must leave or show the "belongs to another wallet" state without flashing the other wallet's data.
- Two wallets on the device are both co-signers of the same multisig: it is shown under each wallet that holds a key in it, and under a wallet that imported it as watch-only. Those wallets share one name and one set of co-signer labels (a rename by one is seen by the other), and deleting it from one wallet removes it only from that wallet; its data stays until the last owning wallet removes it.
- Watch-only multisig (no key on this device): Sign actions are hidden; viewing, receiving, and importing signatures still work.
- Legacy multisigs and pending items created before this change (no recorded owner) are migrated without data loss; nothing is deleted by the migration.
- Rename while offline: allowed, since the name is local only.
- Clipboard or camera permission denied: the user gets a clear message and the manual paste path remains available.
- A scanned QR contains something that is not an address or payment URI (e.g. a PSBT, a Digi-ID request): it is rejected or routed appropriately with an explanation.
- Network mode (mainnet/testnet) mismatch between address and active network is blocked.
- Very large number of historical entries or co-signers (up to the supported maximum N): layouts stay compact and responsive.
- Amount precision: amounts are never rounded in a way that changes the satoshi value; dust-sized outputs are rejected.
- Double-tap on Confirm / Broadcast does not create duplicate proposals or broadcasts.

## Requirements *(mandatory)*

### Functional Requirements

**Wallet scoping**
- **FR-001**: Every multisig wallet, multisig draft, and pending proposal MUST be associated with the wallet(s) that own it (a wallet owns a multisig when it created or imported it, or holds one of its co-signer keys; one multisig may therefore belong to more than one wallet on the device), and the Multisig Wallets list, drafts, and all multisig pages (including Activity, which is derived from the multisig's own address and proposals) MUST show only items belonging to the currently active wallet.
- **FR-002**: Creating or importing a multisig MUST associate it with the currently active wallet.
- **FR-003**: Existing multisigs/drafts created before this feature MUST be attributed to the wallet holding one of their co-signer keys; items that cannot be attributed MUST be hidden from all wallets and MUST be offered, in each wallet, through an "Unassigned multisigs" prompt (shown whenever unassigned items exist, dismissible for the current session) where the user can claim them for that wallet (no data loss).
- **FR-004**: Opening a multisig that belongs to a different wallet (e.g. via a saved link) MUST NOT reveal its contents; the user MUST see an explanatory message and a way back.
- **FR-005**: Deleting a multisig MUST remove it from the active wallet and, once no owning wallet remains, remove its pending proposals and any locally stored records for it, after warning the user about the redeem-script backup.

**Naming**
- **FR-006**: Users MUST be able to rename a multisig wallet and a multisig draft; names are trimmed and must be 1-40 characters; longer names are rejected with a message.
- **FR-007**: Renaming MUST NOT alter the multisig address, redeem script, co-signer keys, or threshold, and MUST persist across restarts.
- **FR-008**: Users MUST be able to edit a co-signer's label (not its key) after the multisig is created.

**Co-signer keys**
- **FR-009**: The dashboard MUST allow viewing the complete public key of every co-signer, with copy, readable grouped formatting, and a QR code; keys MUST never be visually truncated without a way to see the whole value.
- **FR-010**: The user's own key MUST be clearly marked, and watch-only status MUST be shown when this device holds no key.

**Dashboard**
- **FR-011**: The dashboard MUST present, above the fold on a 360×800 viewport: editable name, M-of-N badge, balance, address (copy/QR), and primary actions (Send, Receive, Pending with attention badge). Receive opens a sheet showing the multisig address, its QR code, and a Copy action.
- **FR-012**: Secondary information (co-signers, redeem script & backup, recent activity) MUST be organised in compact, collapsible sections; the redeem-script backup requirement MUST remain visible until the user has downloaded the backup file or copied the redeem script, after which the status shows the date of that backup.
- **FR-013**: The dashboard MUST show loading, offline, and error states distinctly and MUST NOT display an unknown balance as zero.
- **FR-014**: The dashboard MUST surface a "Needs your signature" call to action when applicable.

**Create transaction**
- **FR-015**: The Send page MUST support entering the destination by paste, QR scan (address or payment URI, prefilling amount/label when present), and manual typing.
- **FR-016**: Every destination MUST be validated against the active network's address rules before the user can continue, with inline error messages.
- **FR-017**: The Send page MUST show available balance, estimated fee, and remaining balance; support a "Max" action; and block amounts exceeding the spendable balance or below the dust threshold (0.001 DGB, a conservative floor above the network's relay minimum). A change amount below the dust threshold MUST be added to the fee instead of creating a change output, and Max leaves no change.
- **FR-018**: A review step MUST summarise full recipient address, amount, fee, signatures required, and memo before the proposal is created, and MUST guard against duplicate submission.
- **FR-019**: After creating a proposal, the user MUST be offered to sign it immediately (if holding a key) and to share it with co-signers.

**Pending transactions**
- **FR-020**: Each pending proposal MUST show recipient, amount, fee, memo, age, signature progress (x of M), and a per-co-signer signed/unsigned checklist.
- **FR-021**: Pending proposals MUST be ordered with items needing this user's action first.
- **FR-022**: Users MUST be able to sign, export/share (copy, QR, file), import co-signer signatures (paste, scan, file), broadcast when the threshold is met, retry failed broadcasts, and cancel proposals. Cancelling requires confirmation that states it is local only and that co-signers holding a copy could still sign and broadcast it. Creating a proposal and broadcasting one MUST each guard against duplicate submission (a double tap acts once).
- **FR-023**: Imported signature data MUST be verified to belong to the same proposal and wallet; invalid or foreign data MUST be rejected with an understandable reason. Proposals created before this feature MUST still accept valid co-signer signatures.

**Activity & history**
- **FR-024**: The system MUST provide a unified Activity view for each multisig listing received funds, sent payments, and proposals (awaiting, ready, failed, cancelled), newest first, grouped by date.
- **FR-025**: Each entry MUST show direction, amount, counterparty, memo (if any), date, status, and confirmation state; a detail view MUST show the full transaction ID (copy and open in block explorer), full addresses, fee, signer progress, and status timeline.
- **FR-026**: A broadcast proposal MUST appear once in Activity (as Sent), not as both pending and sent.
- **FR-027**: Activity MUST support filtering (All, Received, Sent, Pending, Failed), meaningful empty states, progressive loading for long histories (the latest 25 entries first, then older entries on demand via "Load more" until the full history is shown), and a degraded offline view of locally known entries.
- **FR-028**: The dashboard MUST show the latest few activity entries with a "View all" link.

**Cross-cutting**
- **FR-029**: All new and revised screens MUST work in light and dark themes on phone-width layouts without horizontal scrolling, with touch targets of at least 44 px and accessible labels for icon-only controls.
- **FR-030**: No private key, seed, or password material may be displayed, logged, or transmitted by any part of this feature; only public data (public keys, addresses, redeem scripts, PSBTs without secrets) is shown or shared.
- **FR-031**: Changes to multisig logic (scoping, renaming, validation, fee/max calculation, signature import checks, activity merging) MUST be covered by automated tests written before implementation.

### Key Entities

- **Multisig Wallet**: A shared-custody wallet defined by threshold M, N co-signers, redeem script, and address; has a user-editable display name and an owning wallet.
- **Co-signer**: A named participant identified by a compressed public key; the label is editable, the key is not; one may be this device's key.
- **Multisig Draft**: A partially configured multisig awaiting co-signer keys; has an editable name and an owning wallet.
- **Pending Proposal**: A payment awaiting signatures, with recipient, amount, fee, memo, signature progress per co-signer, and a status (awaiting, ready, broadcast, failed, cancelled).
- **Activity Entry**: A unified record of a received payment, a sent payment, or a proposal, with direction, amount, counterparty, status, confirmations, and timestamps.
- **Owning Wallet**: The wallet on this device to which a multisig, its drafts, and its records are attached for display and access.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With two wallets on a device, 100% of multisigs and drafts created under one wallet are absent from the other wallet's lists and pages, and fully present again on switching back.
- **SC-002**: A user can rename a multisig in under 10 seconds (no more than 3 taps plus typing) and the new name is visible everywhere on next view and after restart.
- **SC-003**: A user can view and copy any co-signer's full public key within 2 taps from the dashboard, with zero truncated or unreadable keys on screens as narrow as 320 px.
- **SC-004**: On a 360×800 viewport, balance, address, and the three primary actions are visible without scrolling, and the dashboard is at least 30% shorter than the current layout when all sections are collapsed.
- **SC-005**: At least 90% of test participants create a valid payment proposal using paste or scan in under 60 seconds on first attempt, and invalid addresses are caught before submission in 100% of tested cases.
- **SC-006**: Every payment received by or sent from the multisig appears in Activity with correct direction and status; no broadcast proposal is listed twice.
- **SC-007**: A user can identify which proposals need their signature, and complete the sign → share → import → broadcast flow, without leaving the Pending area, with progress visibly updating at each step.
- **SC-008**: All new and changed screens pass a manual check in light and dark themes at 320–768 px widths with no horizontal scrolling and no clipped text.

## Assumptions

- "Wallet switching" refers to the existing multi-wallet support on this device; each wallet has a stable identity that multisigs can be attached to.
- Existing multisigs are attributed by matching the wallet's public key to a co-signer key; watch-only or unattributable legacy items are never deleted and are claimed by the user through the "Unassigned multisigs" prompt.
- Multisig names and co-signer labels are local display labels, not part of the redeem script, and are not shared with co-signers automatically; wallets on the same device that own the same multisig share one name and one set of labels. The backup file may include them as an informational note only; importing a multisig or backup never overwrites existing local labels.
- Received-funds activity relies on the wallet's existing chain data sources; offline mode shows locally known data only.
- QR scanning uses the same camera-based scanning already offered elsewhere in the wallet; where the camera is unavailable, paste remains the fallback.
- Known recipients come from the wallet's existing address book or prior sends, if present; otherwise that selector is omitted.
- Fee estimation uses the wallet's existing fee logic; this feature adds no new fee-policy choices. The multisig Send page uses the same sat/vB fee-rate model and default as the regular Send page, with the transaction size estimated from the actual number of inputs and the multisig script size.
- Out of scope: changing multisig script types, adding/removing co-signers or changing the threshold of an existing multisig, redesign of real-time co-signer rooms, hardware-wallet integration, and fiat display where no rate source exists.
- Visual design follows the wallet's existing design language (theme colours, dark/light modes), refined for compactness and consistency rather than a new brand identity.
