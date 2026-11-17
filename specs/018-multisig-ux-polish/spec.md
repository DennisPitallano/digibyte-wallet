# Feature Specification: Multisig Journey & Signing Experience Polish

**Feature Branch**: `feature/multisig-ux-polish`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "Using your GOD mode UI/UX skills, help me enhance the user journey and experience for multisig creation and signing. Add more indication (online, offline, ready, etc.); signer presentations and data presentations (transaction details for signing etc.); CTAs and icons (like already signed and others); clear instructions but not too much visual noise; simple flows the signers can follow."

## Clarifications

### Session 2026-11-18

- Q: Should "Reviewing" be shown as a signer status even though rooms do not share it today? → A: No. Statuses are Waiting, Signed and Declined plus online/offline presence; nothing new is shared.
- Q: Where should the "needs your signature" attention badge appear with several multisig wallets? → A: On every wallet card in the wallets list plus a total on the multisig home, computed from locally saved pending items (works for locked wallets).
- Q: How quickly should a disconnected co-signer show as Offline? → A: Within about 10 seconds, so a brief reconnect does not flicker the status.
- Q: Should the offline sign screen share the same payment summary and status wording as the live room? → A: Yes; shared summary, status words and icons apply there too, with hand-off mechanics unchanged.
- Q: Should "check before you sign" be a cue or a required step? → A: A one-line advisory cue only; the sign confirmation restates amount and recipient, with no extra required tick.

## Context

Multisig creation (online room, manual public keys, import) and signing (offline Pending list with share/import, and the live signing room from spec 017) are fully functional, but the experience is utilitarian. Co-signers are shown as plain text ("Name ✓ / —"), presence is a single "Reconnecting…" line, payment details are a flat amount and a shortened address, and each screen leaves the person to work out the next step. This feature is a presentation and guidance pass only: it adds no new capabilities, rules, or data, and does not change who can sign, how signatures are verified, or any security behavior. The offline flow and all existing behavior remain available.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See at a glance who is here, who has signed, and what happens next (Priority: P1)

Inside a signing room, a co-signer sees every co-signer as a clear signer card: an avatar with initials, their name (with "You" marked), and one status pill that states exactly where they are: Waiting, Signed, or Declined, shown next to an Online or Offline presence indicator. Online/offline presence is distinct from signing status (a person can be offline and already signed). Above the list, one headline status and one primary button tell the person what to do now ("Your signature is needed" → Review & sign; "2 of 3 signed · Ready to broadcast" → Broadcast; "Waiting for Alex" with nothing to press).

**Why this priority**: The room is the moment of highest confusion and coordination; knowing who is present, who has acted, and what is expected removes the need to ask in a side chat.

**Independent Test**: Join a signing room with two or three devices; change presence (close a tab), sign, decline, and reach the threshold; confirm each state appears on every device's signer list and that the single primary button always matches the person's next valid action.

**Acceptance Scenarios**:

1. **Given** a signing room with several co-signers, **When** any participant views it, **Then** each co-signer appears as one card with avatar, name ("You" for the viewer), a presence indicator (online/offline), and a signing status (waiting, signed, declined) that are visually and textually distinct.
2. **Given** a co-signer disconnects or returns, **When** others look at the room, **Then** that co-signer's presence changes between online and offline within about 10 seconds (going offline) or 5 seconds (coming back online) without a page reload.
3. **Given** the viewer has not yet signed and the threshold is not met, **When** they view the room, **Then** the headline says their signature is needed and the single primary button is "Review & sign".
4. **Given** the viewer has already signed, **When** they view the room, **Then** their own card shows Signed with a check icon, the primary action is no longer "sign", and the headline says what everyone is waiting for (e.g., "Waiting for 1 more signature").
5. **Given** the threshold is reached, **When** any participant views the room, **Then** the headline shows "Ready to broadcast" with a positive treatment and Broadcast as the single primary button, and the signature progress shows complete.
6. **Given** enough co-signers have declined that the payment cannot reach the threshold, **When** participants view the room, **Then** the headline states it can't be completed and the primary action is the sensible exit (go to Pending to cancel).
7. **Given** any status is shown, **When** a person cannot perceive color or uses a screen reader, **Then** every status is also conveyed by an icon and text label and is announced when it changes.

---

### User Story 2 - Understand exactly what I am about to sign (Priority: P1)

Before signing (in the room and on the Pending list), a co-signer sees the payment as a readable summary: a prominent amount, the recipient with the full address available (copyable, grouped for easy comparison), the network fee, the memo, where change goes (and that it returns to the wallet), and the total leaving the wallet. A short "Check before you sign" cue asks them to verify the recipient and amount with the sender. Technical details (inputs, raw transaction) are tucked behind an optional "Details" expander so the default view stays calm. A sign confirmation repeats the amount and recipient in one line.

**Why this priority**: Signing is irreversible and co-signers are the last line of defense; presenting the data clearly is both a UX and safety improvement.

**Independent Test**: Open a pending payment and a signing room; confirm amount, recipient, fee, memo, change, and total are all visible and consistent in both places, the full address can be copied, and technical details are hidden until expanded.

**Acceptance Scenarios**:

1. **Given** a pending payment, **When** a co-signer opens it to sign, **Then** they see amount, recipient (full address, copyable), fee, memo (if any), change handling, and total leaving the wallet in one consistent summary card.
2. **Given** the summary is shown, **When** the viewer expands "Details", **Then** technical information (inputs, transaction identifiers) is shown; when collapsed it adds no clutter.
3. **Given** the viewer taps Sign, **When** the confirmation appears, **Then** it restates the amount and shortened recipient before the person confirms.
4. **Given** the same payment is shown in the Pending list, the signing room, and the offline sign screen, **When** compared, **Then** the same fields appear in the same order with the same wording.

---

### User Story 3 - Follow a guided, low-noise creation journey (Priority: P2)

A person creating a multisig wallet sees a short step indicator (for example: Set up → Invite → Confirm → Ready) and, at each step, one plain-language sentence of instruction and one primary button. In the creation room they see the invite code/link/QR with a clear "Share" action, and signer cards showing who has joined and who is ready, with "Not joined yet" slots for co-signers still to arrive, plus a clear message of what is being waited for ("Waiting for 1 more co-signer to join", "Everyone is ready – create wallet"). Choosing the scheme (e.g., 2-of-3) shows a one-line plain-language explanation ("Any 2 of 3 co-signers must approve each payment"). Manual and import creation paths keep the same step language and CTA conventions.

**Why this priority**: Creation happens once per wallet but sets understanding for every later signature; good guidance prevents mis-configured wallets and abandoned setups.

**Independent Test**: Create a 2-of-3 wallet via the online room from three devices and via manual keys; confirm a step indicator is always visible, each step has one instruction and one primary action, and the waiting messages reflect the actual room state.

**Acceptance Scenarios**:

1. **Given** the creation flow, **When** the person is on any step, **Then** a compact step indicator shows current position and the step has exactly one primary action.
2. **Given** a scheme is selected, **When** it changes, **Then** a one-line explanation of what it means for approvals updates immediately.
3. **Given** a creation room with co-signers joining and marking ready, **When** participants view it, **Then** each joined co-signer shows Joined or Ready, each missing co-signer appears as a "Not joined yet" slot, and the headline states what the room is waiting for (creation rooms drop a disconnected co-signer, so they show no Offline state).
4. **Given** all co-signers are ready, **When** the room is viewed, **Then** the headline and primary button change to the final "Create wallet" action with a positive treatment.

---

### User Story 4 - Consistent status, icons and calls to action across Pending, Activity and Wallets (Priority: P2)

Outside the rooms, a pending payment in the list shows a single clear status (Needs your signature, You signed · waiting for others, Ready to broadcast, Sent, Cancelled/Expired) with a matching icon, and one primary button that fits (Sign, Open room, Broadcast, View). Wallet cards and the multisig home show a small badge when something needs the person's attention ("2 need your signature"). Icons and status wording are identical wherever the same state appears (room, Pending, Activity, wallet card).

**Why this priority**: People return to the app to act on things; surfacing "what needs me" quickly cuts missed signatures and repeated questions.

**Independent Test**: With payments in each state, open Pending, Activity, and the wallets list; confirm each state uses the same icon, label, and primary action everywhere and that attention badges count correctly.

**Acceptance Scenarios**:

1. **Given** a pending payment this person has not signed, **When** they view the Pending list, **Then** it is labeled "Needs your signature" with the primary button "Sign".
2. **Given** a payment this person already signed, **When** it is listed, **Then** it shows "You signed" with a check icon and no "Sign" button, plus how many more are needed.
3. **Given** a payment has an active signing room, **When** it is listed, **Then** it shows a live-room indicator and the primary button "Open room".
4. **Given** one or more payments need this person's signature, **When** they view the wallets list or multisig home, **Then** each affected wallet card shows its count and the multisig home shows the total.
5. **Given** the same state appears in different screens, **When** compared, **Then** its label, icon, and meaning are identical.

---

### User Story 5 - Calm, helpful instructions, empty states and feedback (Priority: P3)

Each multisig screen has one short orienting line, not a wall of text; longer explanations live behind a "How it works" expander or the existing guide. Empty states (no pending payments, no activity, no co-signers yet) explain the next step with one action. Waiting states, success moments (signature added, wallet created, payment sent), and errors use consistent, friendly microcopy and a gentle confirmation cue. The experience works on a phone with one-handed reach for primary actions, in light and dark themes, and respects reduced-motion settings.

**Why this priority**: This ties the experience together, but each earlier story delivers value without it.

**Independent Test**: Walk the whole multisig journey on a phone-sized screen in light and dark themes and with reduced motion on; confirm no screen needs scrolling to find its primary action in normal cases and that no screen shows more than one block of instructional text by default.

**Acceptance Scenarios**:

1. **Given** a screen with explanatory content, **When** it loads, **Then** at most one short orienting sentence is visible by default and the rest is behind an expander.
2. **Given** an empty list (pending, activity), **When** viewed, **Then** a short explanation and one relevant action are shown.
3. **Given** a key success event (signature added, threshold reached, payment sent), **When** it happens, **Then** the person sees a clear confirmation, and motion is skipped when reduced-motion is requested.
4. **Given** a phone-sized screen, **When** any multisig screen is shown, **Then** primary actions are reachable without horizontal scrolling and meet comfortable touch-target size.

---

### Edge Cases

- A co-signer is offline and has already signed: the card shows both facts (offline presence, signed status) without implying the signature was lost.
- Connection to the signing service drops for the viewer: a clear, non-alarming banner explains "Reconnecting – your signature is saved"; actions that need the connection are disabled with the reason, not hidden.
- A very long wallet or co-signer name, or a name with emoji or non-Latin characters: cards truncate gracefully and avatars fall back to a sensible initial.
- Two co-signers share the same display name: cards remain distinguishable (e.g., by a short key fingerprint).
- A payment with no memo or no change: the summary omits the empty fields. Amounts are shown as-is with no special "large amount" flag.
- 7-of-7 style rooms with many co-signers: the list stays scannable on a phone without pushing the primary action off screen.
- Screen reader or keyboard-only use: statuses are announced on change, order is logical, all actions reachable.
- Room expires or is closed while someone is reading: the ended state explains what was saved and gives one next step.
- The person is watch-only (no co-signer key): sign actions are replaced by a clear "view only" status rather than a disabled-looking Sign button.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every co-signer shown in a signing or creation room MUST appear as a signer card with avatar/initials, name ("You" for the viewer), presence (online/offline) and a status (waiting, signed, declined) in signing rooms; in creation rooms, a status of Joined or Ready, plus "Not joined yet" slots for co-signers who have not arrived (creation rooms remove a disconnected co-signer, so no Offline state applies there).
- **FR-002**: Presence and status MUST each be conveyed by icon plus text label (never color alone). Signing status changes (signed, declined) MUST appear on every participant's screen within 5 seconds; a co-signer who disconnects MUST be shown as Offline within about 10 seconds, and a brief reconnect inside that window MUST NOT flip them to Offline.
- **FR-003**: Each room view MUST show one headline status describing the room's state from the viewer's perspective and exactly one primary action matching their next valid step (e.g., Review & sign, Broadcast, Create wallet, or none when only waiting).
- **FR-004**: A viewer who has already signed MUST see an unmistakable "signed" indicator on their card and MUST NOT be offered the sign action again.
- **FR-005**: The signature progress MUST show "X of Y signatures" with a visual bar and flip to a distinct "Ready to broadcast" state when the threshold is met.
- **FR-006**: A single payment summary MUST present amount, recipient (full address, copyable, grouped for readability), fee, memo (when present), change handling, and total leaving the wallet, with identical fields and order in the Pending list, signing room, and offline sign screen.
- **FR-007**: Technical details MUST be collapsed by default behind a "Details" control, and the sign confirmation MUST restate amount and recipient. A one-line "Check before you sign" cue MUST be shown; it is advisory only and MUST NOT add a required tick or extra step before Sign.
- **FR-008**: Creation flows MUST show a compact step indicator and, per step, one orienting sentence and one primary action.
- **FR-009**: Changing the M-of-N scheme MUST show an immediate plain-language explanation of what it means.
- **FR-010**: Pending, Activity, wallet cards, and rooms MUST use one shared vocabulary of states, labels and icons: Needs your signature, You signed, Waiting for others, Ready to broadcast, Live room, Sent, Cancelled/Expired. This vocabulary MUST also apply on the offline sign and import screens, whose copy/QR/file hand-off steps stay unchanged.
- **FR-011**: Each pending item MUST offer one primary button that fits its state (Sign, Open room, Broadcast, View).
- **FR-012**: Every multisig wallet card in the wallets list MUST show an attention badge with the count of payments awaiting the viewer's signature, and the multisig home MUST show the total across wallets; counts MUST be correct and MUST come from locally saved pending items so a locked wallet still shows its badge.
- **FR-013**: Each multisig screen MUST show at most one short orienting sentence by default; longer help MUST sit behind an expander; empty states MUST give a short explanation and one action.
- **FR-014**: Connection loss MUST show a calm banner stating that collected signatures are saved, and connection-dependent actions MUST be disabled with a stated reason rather than hidden.
- **FR-015**: Success moments (signature added, threshold reached, wallet created, payment sent) MUST be confirmed clearly, with motion suppressed under a reduced-motion preference.
- **FR-016**: All new visuals MUST work in light and dark themes, on phone-width screens with touch targets of at least 44 px, with statuses announced to assistive technology and full keyboard operability.
- **FR-017**: Co-signers with identical names MUST remain distinguishable, and long or non-Latin names MUST truncate gracefully with a sensible avatar fallback.
- **FR-018**: This feature MUST NOT alter signing, verification, room rules, security checks, stored data, or what information leaves the device; it only changes presentation, wording, and guidance. New presence/status information shown MUST come from information the rooms already share.
- **FR-019**: A watch-only viewer MUST see a clear "view only" state instead of sign actions.

### Key Entities *(include if feature involves data)*

- **Signer card**: A presentation of one co-signer: avatar/initials, display name, "You" marker, presence, status, short key fingerprint for disambiguation.
- **Room headline**: The viewer-centric one-line state of a room plus its single primary action.
- **Payment summary**: The consistent presentation of recipient, amount, fee, memo, change, and total, with collapsed technical details.
- **Status vocabulary**: The shared set of states with their label, icon, and meaning used across rooms, lists, and wallet cards.
- **Step indicator**: The compact position marker for the creation journey.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a moderated test, at least 90% of first-time co-signers joining a signing room correctly state, without help, what is expected of them next within 10 seconds of the room opening.
- **SC-002**: At least 90% of test participants can tell, within 5 seconds, who has signed, who is offline, and whether the payment is ready to broadcast.
- **SC-003**: A co-signer can go from opening a room link to a confirmed signature in under 60 seconds (excluding any device unlock time).
- **SC-004**: At least 90% of test participants correctly identify recipient, amount, and fee before signing when asked, and none sign the wrong payment in a comparison task.
- **SC-005**: A first-time creator completes online 2-of-3 wallet creation without external instructions in under 5 minutes, with no more than one wrong turn.
- **SC-006**: The same pending state appears with identical label and icon on every screen where it is shown, verified across all states.
- **SC-007**: No multisig screen shows more than one instructional text block by default, and all primary actions are visible without scrolling on a standard phone screen in normal cases.
- **SC-008**: All statuses and actions pass an accessibility review: no color-only meaning, screen-reader announcement of status changes, keyboard operable, and legible contrast in light and dark themes.
- **SC-009**: Questions co-signers ask each other in side channels ("did you sign?", "is it ready?") drop noticeably in follow-up user feedback compared with the current experience.

## Assumptions

- Scope is presentation, wording, iconography, and guidance for the existing multisig creation and signing flows (online and offline); no new capabilities, rules, or stored data.
- Presence (online/offline), ready, signed, and declined are already known to the rooms or derivable from information they already share; where a status is not currently shared, it is out of scope to add new data and the card simply omits that status.
- The existing visual language (colors, typography, press effects, light/dark themes) is retained; this feature unifies and extends it rather than redesigning the app.
- The multisig guide page keeps its content; this feature may link to it but is not a rewrite of it.
- Avatars are generated from names/initials; no profile photos are introduced.
- Success criteria involving user testing will be measured in lightweight moderated sessions with a handful of participants per round.
