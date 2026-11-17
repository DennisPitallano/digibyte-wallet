# Feature Specification: Online Multisig Signing

**Feature Branch**: `feature/multisig-online-signing`

**Created**: 2026-11-17

**Status**: Draft

**Input**: User description: "help me implement online online Multi-Sig signing, we already done that with online creating MultiSig wallet, lets do it for online signing"

## Clarifications

### Session 2026-11-17

- Q: How long does a signing room live, and must co-signers be online together? → A: Live sessions only (co-signers online together), with a configurable lifetime that defaults to 30 minutes.
- Q: If the co-signer who opened the room leaves or disconnects, should the room stay open for the others? → A: Yes. The room stays open until its lifetime ends or the opener explicitly closes it; a disconnected opener is shown as offline like any participant.
- Q: Can two co-signers each open their own room for the same payment at the same time? → A: No. One active room per payment; a co-signer who tries to open another is offered to join the existing one (after proving they are a co-signer).
- Q: Which room lifetimes can the opener choose, and what is the default service maximum? → A: Choices of 15, 30, 60, or 120 minutes (default 30); service maximum defaults to 120 minutes and is operator-adjustable. Choices above the maximum are not offered.
- Q: Should a watch-only device (no co-signer key) be allowed to join a signing room? → A: No. Co-signers only; watch-only devices see online signing as unavailable and use the offline flow.
- Q: How should the service limit attempts to join or open rooms? → A: Per connection, at most 5 failed join attempts per minute and 10 rooms opened per hour; exceeding either shows "Too many attempts, try again shortly".
- Q: What may the service record in its logs about signing rooms? → A: Only anonymous counts and events (rooms opened/ended and reason, participant counts, signatures counted, broadcast outcome, rate-limit hits, errors); never keys, addresses, amounts, payment data, invite codes, or display names.

## Context

Co-signers can already create a multisig wallet together online: one person opens a room, shares a 6-character invite code (or link), everyone joins, marks themselves ready, and each device builds the same wallet locally. Signing a payment from that wallet is still offline only: the proposer signs, exports the partially signed transaction (copy, QR, or file), sends it to each co-signer through some outside channel, and imports each signed copy back before broadcasting. This feature brings the same "room" experience to signing a pending payment, so co-signers can review, sign, and broadcast together in real time without passing files around. The offline flow stays available and unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Collect signatures for a payment in a live signing room (Priority: P1)

A co-signer has a pending proposal in their multisig (for example a 2-of-3 payment they just created and signed). From the proposal they choose "Sign together online". The wallet opens a signing room for that proposal and shows an invite code, a shareable link, and a QR code. The other co-signers open the link or enter the code in their own wallet, which has the same multisig. Each joiner sees the payment details (recipient, amount, fee, memo, signatures so far), reviews them, and taps Sign. Their wallet signs locally after the usual unlock/confirmation, and the signature appears for everyone in the room within seconds. The signer checklist updates live for all participants. When the threshold is reached, everyone sees "Ready to broadcast".

**Why this priority**: This is the core value: replacing the copy/QR/file round-trip between co-signers with a single shared session. Without it there is no feature.

**Independent Test**: With two devices holding keys of the same 2-of-3 multisig, create a proposal on device A, open a signing room, join from device B by code, sign on B, and confirm both devices show 2 of 2 required signatures and a complete, valid transaction, without any manual export or import.

**Acceptance Scenarios**:

1. **Given** a pending proposal in a multisig this wallet belongs to, **When** the user chooses "Sign together online", **Then** a signing room opens for that proposal and shows an invite code, a copyable link, a QR code, the expiry time, and the live signer checklist.
2. **Given** an open signing room, **When** a co-signer enters the code or opens the link in a wallet that holds a key of the same multisig, **Then** they join the room and see the full payment details and current signature progress before being asked to sign.
3. **Given** a joined co-signer reviewing the payment, **When** they tap Sign and confirm, **Then** their wallet signs on their device, and within 5 seconds every participant sees that co-signer marked as signed.
4. **Given** a signature arrives from another participant, **When** a wallet receives it, **Then** that wallet checks the signature is valid for this exact proposal and this co-signer's key before showing it as signed; invalid signatures are discarded and the room shows a warning naming the participant.
5. **Given** a co-signer who does not want to sign, **When** they tap Decline, **Then** all participants see that co-signer marked as declined, and the proposer can still collect the threshold from others.
6. **Given** the room has collected enough signatures, **When** any participant views it, **Then** each participant's local copy of the proposal is updated with all collected signatures and shows "Ready to broadcast".

---

### User Story 2 - Broadcast once the threshold is reached (Priority: P1)

When enough co-signers have signed, one participant broadcasts the payment. Everyone in the room immediately sees that it was sent, with its transaction ID, and their local pending proposal moves to Activity as Sent, so no one broadcasts it twice or is left with a stale pending item.

**Why this priority**: Collecting signatures without completing the payment leaves the user halfway; the whole point is to move funds.

**Independent Test**: Continue from Story 1: tap Broadcast on either device and confirm the transaction ID appears on both devices, both mark the proposal as Sent, and the Broadcast action is no longer offered anywhere in the room.

**Acceptance Scenarios**:

1. **Given** a room whose proposal has reached the threshold, **When** a participant taps Broadcast, **Then** the payment is broadcast from that participant's device and every participant sees "Broadcast" with the transaction ID within 5 seconds.
2. **Given** one participant has broadcast, **When** other participants look at the room or their pending list, **Then** the Broadcast action is gone and the proposal shows as Sent (not pending) on their device.
3. **Given** two participants tap Broadcast at nearly the same moment, **When** both requests are processed, **Then** the payment is sent once and both participants see the same transaction ID; neither sees a confusing failure.
4. **Given** the broadcast fails, **When** the failure is shown, **Then** all participants see a human-readable reason and the proposal remains complete and can be retried by any participant.

---

### User Story 3 - Join a signing room as an invited co-signer (Priority: P2)

A co-signer receives a link or code from the proposer (by chat, email, or in person). They open the wallet, unlock it, and land directly in the room for that proposal. If the payment is new to their device, it is added to their pending list automatically so they keep it even if they leave the room.

**Why this priority**: Joining has to be effortless and safe, but it depends on the room from Story 1 existing.

**Independent Test**: On device B, open the invite link while locked, unlock, and confirm it lands in the right room for the right multisig, and that the proposal appears in B's Pending list after leaving the room.

**Acceptance Scenarios**:

1. **Given** an invite link, **When** a co-signer opens it, **Then** the wallet asks them to unlock (if locked) and then opens the room directly, with the code prefilled.
2. **Given** the co-signer's device has several wallets, **When** they join, **Then** the room is joined with the active wallet if it holds a key of the multisig; if it does not but another wallet on the device does, the user is offered a one-tap switch to that wallet (choosing which one when several do) and the join continues; if none does, joining is refused with a clear message.
3. **Given** a co-signer joins and their device did not yet have the proposal, **When** they join, **Then** the proposal is saved to their local pending list, so leaving the room keeps it available for offline signing later.
4. **Given** a co-signer's device holds a different version of the same proposal (e.g. with signatures imported offline), **When** they join, **Then** signatures from both sides are combined and shared with the room, never lost.

---

### User Story 4 - Recover from disconnection and room end (Priority: P2)

Mobile connections drop. If a participant loses connection, the room keeps their place and their signature, and they rejoin automatically when the connection returns. If the room ends (expiry, the proposer closes it, or the service becomes unreachable), nothing already signed is lost and everyone can continue with the existing offline share/import flow.

**Why this priority**: Without it, a flaky connection would cost users their progress and trust; the existing offline flow is the safety net.

**Independent Test**: Join a room from device B, sign, turn off B's network for 30 seconds, turn it back on, and confirm B is back in the room with its signature still shown; then close the room and confirm both devices keep every collected signature in their local proposal.

**Acceptance Scenarios**:

1. **Given** a participant loses connection, **When** the connection returns before the room ends, **Then** they rejoin automatically and see the current state, including signatures added while they were away.
2. **Given** a participant is disconnected, **When** others look at the room, **Then** that participant shows as "offline" rather than being removed, and their signature (if given) still counts.
3. **Given** the room ends for any reason, **When** participants see the end notice, **Then** the reason is shown and their local proposal keeps every signature collected, with the offline Share/Import actions offered to finish.
4. **Given** the online signing service is unreachable, **When** the user opens a pending proposal, **Then** "Sign together online" is shown as unavailable with a short explanation, and all offline actions (sign, share, import, broadcast) keep working.

---

### User Story 5 - Proposer controls the room (Priority: P3)

The person who opened the room can see who has joined, close the room early, or open a new room for the same proposal later. Closing a room never cancels the proposal.

**Why this priority**: Useful control and clarity, but the core flow works without it.

**Independent Test**: Open a room, close it, confirm participants are notified and the proposal is still pending; open a new room for the same proposal and confirm the old code no longer works and the new one does.

**Acceptance Scenarios**:

1. **Given** the room creator, **When** they tap Close room and confirm, **Then** all participants are notified, the code stops working, and the proposal stays pending on every device with its signatures.
2. **Given** a proposal whose room ended, **When** any co-signer chooses "Sign together online" again, **Then** a new room with a new code is opened for the same proposal.
3. **Given** a proposal already has an active room (opened from this or any other co-signer's device), **When** a co-signer chooses "Sign together online", **Then** they are offered to join that room instead of opening a second one.

---

### Edge Cases

- A person without a key in the multisig obtains the code: they cannot join or see the payment details; the room reveals nothing beyond "invalid or expired code".
- A participant joins with the same co-signer key twice (two tabs or two devices): only one active presence per co-signer key; the newer one replaces the older, and a signature is counted once.
- The proposal on a joiner's device differs in recipient, amount, fee, or inputs from the room's proposal (tampered or a different proposal sharing an ID): joining is refused with a clear reason; no signature is made.
- A signature arrives for a co-signer key that is not in the multisig, or does not verify: it is discarded and never shown as signed.
- The payment's inputs are spent elsewhere (another proposal was broadcast first) while the room is open: the room shows the proposal can no longer be completed and suggests cancelling it locally.
- The room reaches its expiry while a participant is mid-confirmation: their signature is still saved locally and can be shared offline.
- A co-signer has already signed offline before joining: their existing signature is shown as signed immediately, and they are not asked to sign again.
- The proposal was cancelled locally on one device: that device cannot open or join a room for it; other co-signers are unaffected (cancel stays local only, as today).
- A watch-only copy of the multisig (no key on this device): "Sign together online" is shown as unavailable with a short explanation, and joining a room by code or link is refused; the offline flow (import, broadcast) remains available.
- The room creator closes the app or loses connection mid-session: the room continues for the others, who can still sign and broadcast; the creator rejoins automatically when back.
- All co-signers are needed (N-of-N) and one declines: the room shows the payment cannot reach the threshold.
- Someone repeatedly enters wrong codes, or opens many rooms: after 5 failed join attempts in a minute, or 10 rooms in an hour, further attempts from that connection are refused with "Too many attempts, try again shortly".
- Several rooms are open at once for different proposals of the same multisig: each is independent and clearly labelled with its payment.

## Requirements *(mandatory)*

### Functional Requirements

**Opening and joining**

- **FR-001**: Users MUST be able to open an online signing room from any pending proposal of a multisig that belongs to the active wallet, including proposals created before this feature.
- **FR-002**: Each room MUST be tied to exactly one proposal and one multisig, and MUST present an invite code (same short, unambiguous format as wallet-creation rooms), a copyable link, and a QR code.
- **FR-003**: Opening the invite link MUST take the user to the room with the code prefilled, after unlocking the wallet if needed.
- **FR-004**: Only participants who prove, from their device, that they hold one of the multisig's co-signer keys MUST be able to join and see payment details. Anyone else MUST receive the same "invalid or expired code" response as for a non-existent code.
- **FR-005**: When joining, the wallet MUST check that the room's proposal matches the multisig it holds (same address, redeem script, threshold, and co-signer keys) and, if it already holds that proposal, the same recipient, amount, fee, and inputs; any mismatch MUST block joining and signing with a clear reason.
- **FR-006**: A joiner whose device did not yet hold the proposal MUST have it saved to the local pending list of the matching wallet on join.

**Reviewing and signing**

- **FR-007**: Before signing, every participant MUST see recipient (full address), amount, fee, memo, change handling, and the current per-co-signer signature checklist.
- **FR-008**: Signing MUST happen only on the participant's own device, after explicit confirmation and any unlock the wallet already requires for signing. Private keys, seed material, and passwords MUST never leave the device or pass through the room.
- **FR-009**: The room MUST show live, for every co-signer: not joined, joined, offline, signed, or declined, and the overall progress (x of M).
- **FR-010**: Each wallet MUST verify every signature it receives against the proposal and the claimed co-signer key before counting it; invalid signatures MUST be discarded and flagged in the room.
- **FR-011**: Signatures collected in the room MUST be merged into each participant's local copy of the proposal as they arrive, so leaving or losing the room never loses them.
- **FR-012**: Signatures already present on any participant's local copy (for example imported offline) MUST be shared with the room on join and counted once.
- **FR-013**: Participants MUST be able to decline to sign; a decline MUST be visible to all and MUST NOT prevent others from signing.

**Broadcasting**

- **FR-014**: Once the threshold is met, every participant MUST be offered Broadcast, and the payment MUST be broadcast at most once even if several participants act at the same time.
- **FR-015**: After a successful broadcast, all participants MUST see the transaction ID, and each participant's local proposal MUST move to Activity as Sent (not listed as pending as well).
- **FR-016**: A failed broadcast MUST be shown to all participants with a human-readable reason, and the proposal MUST stay complete and retryable by any participant.

**Room lifecycle and resilience**

- **FR-017**: Rooms are live sessions: co-signers sign while online at the same time. The room's lifetime MUST be configurable, defaulting to 30 minutes: the person opening the room can choose 15, 30, 60, or 120 minutes (default 30), limited by a service maximum that defaults to 120 minutes and that the operator can change; choices above the current maximum MUST NOT be offered, the service MUST reject any longer request, and it MUST apply the default when none is chosen. When the lifetime ends, the room ends, participants are notified, and the service discards the room's data. The remaining time MUST be visible to all participants.
- **FR-018**: A participant who disconnects MUST keep their place and signature, be shown as offline to others, and automatically rejoin with the current state when their connection returns while the room is open.
- **FR-019**: The room creator MUST be able to close the room; closing MUST NOT cancel or alter the proposal on any device. The room MUST NOT end when its creator leaves the page or disconnects; it stays open for the other participants until its lifetime ends or the creator explicitly closes it, and the creator is shown as offline like any other participant. Any co-signer MUST be able to open a new room for a proposal whose room has ended.
- **FR-019a**: There MUST be at most one active room per proposal. When any co-signer (on any device) chooses "Sign together online" for a proposal that already has an active room, they MUST be offered to join that room instead, subject to the same co-signer proof as FR-004; no second room is created.
- **FR-020**: When a room ends for any reason, participants MUST see why, and the offline actions (sign, share, import, broadcast) MUST remain available with all collected signatures.
- **FR-021**: When the online signing service is unreachable, "Sign together online" MUST appear as unavailable with an explanation, and no balance, receive, send, history, or offline signing flow may be blocked or slowed: those pages MUST NOT connect to the online signing service at all, and only opening "Sign together online" may try to reach it.
- **FR-022**: If the payment can no longer be completed (its inputs were spent elsewhere, or too many co-signers declined to reach the threshold), the room MUST say so.

**Privacy and safety**

- **FR-023**: The room MUST carry only public data: public keys, the multisig's public details, the unsigned/partially signed payment, signatures, display names, and participant status. It MUST NOT receive private keys, seeds, passwords, or wallet labels the user did not choose to share.
- **FR-024**: The online signing service MUST NOT keep any room data after the room ends, and MUST NOT be able to sign, alter, or broadcast a payment on its own; every participant's wallet independently verifies everything it receives.
- **FR-025**: Display names shown in the room MUST be chosen by each participant on join (prefilled with their local label if they wish) and MUST NOT overwrite any other device's local co-signer labels.
- **FR-026**: Opening a room, signing, and broadcasting MUST each guard against duplicate submission (a double tap acts once).
- **FR-027**: The service MUST rate-limit each connection to at most 5 failed join attempts per minute and at most 10 rooms opened per hour; requests over either limit MUST be refused, and the wallet MUST show "Too many attempts, try again shortly" without revealing whether a code exists.
- **FR-028**: Service logs and metrics about rooms MUST be limited to anonymous counts and events: rooms opened and ended (with reason), participant counts, signatures counted, broadcast outcome, rate-limit hits, and errors. They MUST NOT contain public keys, addresses, amounts, payment data, signatures, invite codes, room links, or display names.

### Key Entities

- **Signing Room**: A short-lived online session for one pending proposal; has an invite code, link, creator, expiry, status (open, complete, broadcast, closed, expired), and its participants.
- **Room Participant**: A co-signer present in a room (watch-only holders cannot join); identified by their co-signer public key, with a chosen display name and a status (joined, offline, signed, declined).
- **Pending Proposal**: The existing payment awaiting signatures (recipient, amount, fee, memo, inputs, signatures per co-signer, status); the room reads it and writes collected signatures and the broadcast result back into each participant's local copy.
- **Co-signer Signature**: A signature from one co-signer key over the proposal; only counted once verified by the receiving wallet.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two co-signers who are both online can complete a 2-of-3 payment (open room, join, sign, broadcast) in under 2 minutes, with no copy, file, or QR exchange of transaction data.
- **SC-002**: A signature or status change made by one participant is visible to every other connected participant within 5 seconds in 95% of cases.
- **SC-003**: 100% of signatures collected in a room are still present in each participant's local proposal after the room ends, a participant disconnects, or the app is restarted.
- **SC-004**: 0 invalid or foreign signatures are ever counted toward the threshold, and 0 payments are broadcast more than once from rooms.
- **SC-005**: A person without a co-signer key of the multisig (including a watch-only holder) learns nothing about the payment from an invite code in 100% of attempts.
- **SC-006**: A participant who loses connection for up to 60 seconds is back in the room with current state without any manual action in 95% of cases.
- **SC-007**: With the online signing service down, every offline multisig action and all core wallet flows still work, and users see why online signing is unavailable.
- **SC-008**: At least 90% of test users who have used the online creation room complete an online signing session on their first attempt without help.

## Assumptions

- This builds on the existing real-time multisig room service used for wallet creation (invite codes, links, live participant state); online signing is an optional convenience next to, not a replacement for, the offline share/import flow.
- The relay role is the same kind already used for creation rooms: it passes public coordination data between co-signers and is never a required participant in signing (the offline flow always works without it). The plan will confirm this against Constitution Principle II.
- Participants use their own devices and wallets; there is no push notification to co-signers who are not in the app. The proposer shares the code or link through their own channel.
- Co-signers in a room may each hold the multisig in a different wallet on their device; the wallet that holds the matching key is used.
- Supported multisig sizes are the same as today (up to 7 co-signers).
- "Configurable" lifetime means a choice offered to the person opening the room (15/30/60/120 minutes, default 30), capped by an operator-set maximum on the service (default 120 minutes). Co-signers who cannot be online together keep using the offline share/import flow; a room that holds a payment for hours or days so co-signers can sign at different times is out of scope.
- Fee and recipient are fixed when the proposal is created; rooms do not allow editing a proposal. Changing a payment means cancelling it locally and creating a new proposal.
- Hardware-wallet signing, changing multisig membership, and redesign of the creation rooms are out of scope.
