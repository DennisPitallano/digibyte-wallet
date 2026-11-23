# Feature Specification: Multisig Wallet Experience (List, Navigation, Pending, Signing Room)

**Feature Branch**: `026-multisig-wallet-experience`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "Using your GOD mode UI/UX skills, help me enhance and update the MultiSig Wallet Managements. (1) Enhance the MultiSig wallet list. (2) On the MultiSig Dashboard, should we display to where the Multisig wallet belongs to, should we change the Shell Menu? When the users click the Wallet on the shell menu it go back from the start again, should we change it to MultiSig Wallet menu instead? Suggest for the Best UI/UX, user journey. (3) Enhance the Pending transaction screen, make it modern and easy for user to understand and navigate. (4) Enhance the Sign Together Online Room. Make it modern and easy for the signer to understand and navigate."

## Clarifications

### Session 2026-11-23

- Q: Should the bottom menu change while a shared wallet is open, or stay the personal menu? → A: Change it as recommended: same five positions, retargeted to the shared wallet (Overview, Activity, Scan, Pending, More).
- Q: Should the bottom menu stay visible while a person is inside a signing room? → A: No. It is hidden from the moment the person is in the room until they leave its result screen; the main button is pinned to the bottom edge and leaving is done with Back or the room menu. The join screen keeps the menu.
- Q: When a person taps a pending payment or "Review & sign", should it open as its own screen or as a sheet over the list? → A: Its own screen for one payment, with the full summary, the main button and all other actions; Back returns to Pending, and the screen can be opened directly (from a scan, the room's result screen or a card).
- Q: In the personal wallet, should the More tab show a count when a payment in one of that wallet's shared wallets needs the person? → A: Yes. The count shows on the More tab, on the Multisig row inside the More list, and on the home shortcut.
- Q: After a payment is sent, should it stay in the Pending list for a while? → A: No. A sent payment appears only in Activity; sending shows a confirmation with a link to it there. Pending's folded group holds cancelled payments only, and failed payments stay under "needs you".
- Q: Inside a shared wallet, which wallet should a scanned DigiByte address or payment request pay from? → A: The shared wallet that is open: its send screen opens with the scanned recipient and amount filled in. Digi-ID logins, proposals and room invites are routed as today.
- Q: Should the changed multisig screens be translated into Spanish, or only the bottom menu? → A: Only the bottom menu: its new labels and counts are provided in English and Spanish. The multisig screens stay English only, as today.
- Q: Should a payment that can no longer go through (its coins were already spent) still count as "needs you"? → A: No. Once this device knows, it is not counted on the menu, on Home or on the list of shared wallets, and on Pending it sits in its own group, "Can't go through", between "Waiting for others" and "Cancelled", where the one action is Cancel. The device learns this when the Pending list or the payment's own screen checks the chain; until then the payment is counted as before.
- Q: Should the final "slide to send" control sit on the Pending list card or only on the payment's own screen? → A: Only on the payment's own screen. The card's button reads "Review & send" and opens that screen; retrying a failed send works the same way. The Pending list never signs, sends or retries by itself.

## Context

A multisig ("shared wallet") lives inside one of the person's own wallets: it is reached through More → Multisig and is listed only while that wallet is active. Four things make the current experience harder than it should be:

- **The list says little.** Each card shows a name, the scheme (for example 3-of-5) and a shortened address. It does not show the balance, whether anything is waiting, whether the shared wallet is backed up, or whether this device can sign.
- **People lose their place.** Inside a shared wallet the bottom menu still belongs to the personal wallet. Its Activity and Scan sit right below the shared wallet's own Activity and Scan buttons, with the same names and different meanings. Tapping Wallet leaves the shared wallet completely; getting back takes three taps (More → Multisig → the wallet). The dashboard does not say which personal wallet the shared wallet belongs to.
- **The Pending screen puts tools before work.** "Join a room with a code" and "Got a payment proposal?" take the top of the screen, above the payments that need attention. Each payment then offers five buttons of similar weight (Sign, Open room, Share, Import signature, Cancel), and opening a panel pushes a large QR code into the list.
- **The signing room is one long scroll.** The amount is shown twice, the invite a host needs first is at the bottom, the list of signers is five near-identical rows, and Close room and Leave are small text links at the very end.

This feature is a presentation, navigation and guidance pass. It does not change who can sign, how signatures are checked, the room rules, what is stored, or what leaves the device.

## Recommended direction *(answers to the questions in the request)*

**Should the dashboard show which wallet the shared wallet belongs to?** Yes. Every screen inside a shared wallet shows a context line: the shared wallet's name, its scheme, and "in *wallet name*" with that wallet's colour. Today's chip shows only the personal wallet's name, cut short ("TREASURY ADD…"), and the dashboard shows nothing.

**Should the bottom menu change inside a shared wallet?** Yes, in a limited way. The menu keeps its shape and its five positions, so nothing moves under the thumb, but while a shared wallet is open the positions act on that shared wallet:

| Position | Personal wallet (today) | Inside a shared wallet (proposed) |
|----------|-------------------------|-----------------------------------|
| 1 | Wallet | Overview (this shared wallet's dashboard) |
| 2 | Activity | Activity (this shared wallet's) |
| 3 | Scan | Scan (same scanner; an address or payment request pays from this shared wallet) |
| 4 | Request | Pending (with a count of payments that need this person) |
| 5 | More | More (unchanged) |

Why this is better than leaving the menu alone:

- It removes the trap of two "Activity" and two "Scan" controls with different meanings on one screen.
- The first position never throws the person out. It takes them to the top of where they are.
- The dashboard gets simpler: Pending, Activity and Scan no longer need their own tiles, leaving Send and Receive as the two clear actions.

Why not replace the menu with something entirely different: a bottom menu that changes shape is disorienting. Keeping the positions and changing only what they point to is the least surprising form of a context switch, provided the switch is obvious. So the menu must also look different in this mode (the shared wallet's name shown with it and a distinct accent), and leaving must be one obvious tap.

**How does the person leave, and come back?** The context line opens a switcher that lists the personal wallet ("Back to *wallet name*") and this wallet's other shared wallets. From the personal wallet's home, the existing Multisig shortcut shows the count of payments waiting and reopens the shared wallet the person used last. The same count shows on the More tab, so a waiting payment is visible from every personal screen.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Always know where I am, and never get thrown out (Priority: P1)

A person opens the shared wallet "HalfThePot". At the top they see its name, "3-of-5", and "in Treasury" with Treasury's colour. The bottom menu now reads Overview, Activity, Scan, Pending, More, and looks visibly different from the personal menu. They tap Pending, then Activity, then Overview: each one stays inside HalfThePot. When they want to leave, they tap the context line and choose "Back to Treasury".

**Why this priority**: Losing one's place is the problem the request describes most directly, and every other screen in this feature sits inside this navigation.

**Independent Test**: Open a shared wallet and tap each menu position in turn; confirm every destination belongs to that shared wallet, that the context line is on every screen, and that leaving and returning each take one or two taps.

**Acceptance Scenarios**:

1. **Given** a shared wallet's dashboard, pending list, a pending payment's own screen, activity, send screen or signing room is open, **When** the person looks at the top of the screen, **Then** they see the shared wallet's name, its scheme and the name and colour of the personal wallet it belongs to, without the names being cut to fewer than 12 characters on a phone-width screen.
2. **Given** a shared wallet is open, **When** the person looks at the bottom menu, **Then** it shows Overview, Activity, Scan, Pending and More in the same five positions as the personal menu, with a visible cue that it now belongs to the shared wallet.
3. **Given** a shared wallet is open, **When** the person taps Overview, Activity or Pending, **Then** they land on that shared wallet's own dashboard, activity or pending list.
4. **Given** payments in this shared wallet need the person (their signature, ready to broadcast, or a failed broadcast to retry), **When** the menu is visible, **Then** Pending shows their count; with none, it shows no count.
5. **Given** a shared wallet is open, **When** the person taps the context line, **Then** a switcher lists "Back to *wallet name*" and the other shared wallets of that personal wallet, each with its scheme and any attention count.
6. **Given** the person leaves to the personal wallet, **When** they view its home, **Then** the menu is the personal one again (Wallet, Activity, Scan, Request, More) and nothing of the shared wallet's context remains on screen.
7. **Given** the multisig list, create, import or guide screens are open (no single shared wallet is selected), **When** the person looks at the menu, **Then** it is the personal one, with More marked as current, as today.
8. **Given** the shared wallet's dashboard, **When** the person looks at its action buttons, **Then** no button repeats a destination that the menu already offers under the same name.

---

### User Story 2 - See what needs me in Pending and act with one tap (Priority: P1)

A co-signer opens Pending. The first thing on screen is the payment that needs them, in a compact card: a status ("Needs your signature"), the amount, a short recipient, "0 of 3 signatures" with who has signed, and one button, "Review & sign". Payments waiting on others come next; cancelled ones are folded away at the bottom. Tapping the card opens that payment's own screen, where sharing, importing a signature, opening a room and cancelling live, instead of five buttons in a row on the list. Adding a proposal from a co-signer is a single "Add a proposal" button at the top.

**Why this priority**: Pending is where signing work happens every day. The current screen shows the tools first and the work second, with no clear main action.

**Independent Test**: With payments in each state (needs my signature, signed and waiting, ready to broadcast, live room, cancelled, failed), open Pending and confirm the order, the single main button on each card, and that every existing action can still be reached.

**Acceptance Scenarios**:

1. **Given** payments in several states, **When** Pending opens, **Then** they are grouped under plain headings in this order: needs you (your signature, ready to broadcast, or a failed broadcast to retry), waiting for others, then cancelled; a group with nothing in it is not shown.
2. **Given** at least one payment needs the person, **When** Pending opens on a phone-width screen, **Then** the first such payment and its main button are visible without scrolling.
3. **Given** any open payment card, **When** the person looks at it, **Then** it shows one status, the amount, a shortened recipient, signature progress ("X of Y") with which co-signers have signed, its age, and exactly one main button fitting its state: Review & sign, Open room, Get signatures, Review & broadcast, or Review & retry. Every one of these opens the payment's own screen (or the room); none signs or sends from the list.
4. **Given** a payment's own screen, **When** the person looks below the main button, **Then** every other action available today is offered with a one-line explanation: Sign together online, Share with co-signers, Add a signed copy, Cancel; Cancel is visually separated as the destructive choice.
5. **Given** the person taps a card (not its button), **When** the payment's own screen opens, **Then** they see its status, the full payment summary (full address, fee, total leaving the wallet, note, details), who has signed, and the same main button; Back returns to Pending at the position they left.
6. **Given** the person taps "Review & sign" on a card, **When** the payment's own screen opens, **Then** it shows the full payment summary with the check-before-you-sign cue and a confirm step that restates amount and recipient, as today.
7. **Given** the person has signed and more signatures are needed, **When** they tap "Get signatures", **Then** the payment's own screen opens and they are offered two clearly described paths: sign together online now (a live room) or send the proposal to co-signers (QR, copy, file), plus adding a signed copy they got back.
8. **Given** the top of Pending, **When** the person taps "Add a proposal", **Then** they can scan a QR code, paste, choose a file, or join a room with a code, all from one place.
9. **Given** nothing is pending, **When** Pending opens, **Then** a short explanation is shown with two actions: create a payment, and add a proposal from a co-signer.
10. **Given** an action succeeds or fails (proposal saved, signature added, copied, broadcast failed), **When** the result is reported, **Then** it appears as a brief message that does not push the list down and is announced to assistive technology; a failure stays until dismissed.
11. **Given** a payment whose coins have already been spent, **When** it is listed, **Then** it says so in plain words, its main button reads "Review & cancel" and opens the payment's own screen where Cancel is offered, and it cannot be signed or sent.

---

### User Story 3 - Understand the signing room at a glance and finish in it (Priority: P1)

A co-signer opens a room link. One screen tells them: what is being paid (amount, recipient, total, shown once), where the group is (a three-step tracker: Invite → Sign → Broadcast), what they should do now (one headline with one main button that stays reachable while they scroll), and who has signed. The host sees the invite (code, share, copy link, QR) directly under the headline while people are still missing; once enough co-signers are present it folds into one row. The time left and the connection state sit in a small room bar at the top, and Leave and Close room are in a clearly labelled room menu. The bottom menu is hidden while they are in the room, so the main button owns the bottom of the screen.

**Why this priority**: The room is used live, with other people waiting. Seconds spent hunting for the invite or the sign button are felt by the whole group.

**Independent Test**: Run a room on three devices through invite, sign, decline, threshold, broadcast, disconnect and expiry; confirm on each device that the tracker, headline, main button and signer list match the room's state and that the main button never needs scrolling to reach.

**Acceptance Scenarios**:

1. **Given** a person is in a room, **When** they view it, **Then** the amount, recipient and total leaving the wallet each appear once in a single payment summary, and the full address, fee, note and technical details can be opened from it.
2. **Given** a room in any state, **When** a person views it, **Then** a three-step tracker shows Invite, Sign and Broadcast with the current step marked: Invite while fewer co-signers than the threshold have joined or signed, Sign while signatures are being collected, Broadcast once the threshold is reached, and complete after the payment is sent.
3. **Given** the viewer has a valid next action (review and sign, broadcast, open the payment), **When** they scroll anywhere in the room, **Then** that one main button remains visible, pinned to the bottom edge of the screen; the bottom menu is not shown in the room.
4. **Given** the viewer is the host and fewer co-signers than the threshold have joined or signed, **When** they view the room, **Then** the invite (room code, a share action, copy link, copy code and QR code) appears directly under the headline.
5. **Given** enough co-signers have joined or signed to reach the threshold, or the viewer is not the host, **When** they view the room, **Then** the invite is folded into one row showing the code, and can be opened again with one tap.
6. **Given** the signer list, **When** a person views it, **Then** it shows "X of Y signatures" with how many more are needed, the viewer first, then others ordered signed, online, offline; each signer has a name, presence and signing status shown with icon and text.
7. **Given** a room is open, **When** a person views it, **Then** the time left and the connection state are visible at the top without scrolling; when 5 minutes or less remain the time is emphasised and announced once.
8. **Given** a person opens the room menu, **When** they read it, **Then** Leave says that signatures already collected are saved on this device, and Close room (host only) says it ends the room for everyone without cancelling the payment; Close room asks for confirmation.
9. **Given** the viewer can sign, **When** they look for a way to refuse, **Then** "Decline to sign" is available next to the review, less prominent than signing, and asks for confirmation as today.
10. **Given** a room ends (broadcast, closed, expired, or can no longer complete), **When** a participant views it, **Then** a result screen states what happened, how many signatures were kept, the transaction ID with a copy action if it was sent, and one button to the sensible next place.
11. **Given** a person opens the join screen, **When** they enter a code, **Then** the six characters can be typed, pasted in one go or scanned, the screen says which wallet they are joining as, and the join button activates only when the code is complete.
12. **Given** the join fails because the payment belongs to another of the person's wallets, **When** the message is shown, **Then** it offers those wallets to switch to, as today.
13. **Given** the connection drops, **When** the person views the room, **Then** a calm notice says collected signatures are saved, and the main button is disabled with the reason stated, as today.

---

### User Story 4 - Read the health of every shared wallet from the list (Priority: P2)

A person opens the multisig list. The heading says whose shared wallets these are ("Shared wallets in Treasury"). Each card shows the name, the scheme, the balance with its value in the person's currency, a one-line status ("1 payment needs your signature", "Ready to broadcast", "Live room open", "Waiting for 2 co-signers", or nothing when all is quiet), a warning if the shared wallet has not been backed up, and "View only" if this device cannot sign. Wallets that need attention come first. Create and Import are two clear actions.

**Why this priority**: The list is the entry point and should answer "is anything waiting for me, and is my money there?" without opening each wallet. It ranks below the three stories above because it is passed through more quickly.

**Independent Test**: With several shared wallets in different states (pending for me, ready to broadcast, not backed up, view only, empty, balance unavailable), open the list and confirm each card states its condition correctly and attention items are first.

**Acceptance Scenarios**:

1. **Given** the list, **When** it opens, **Then** its heading names the personal wallet these shared wallets belong to, with that wallet's colour.
2. **Given** a shared wallet card, **When** the person views it, **Then** it shows name, scheme, balance in DGB and in the person's currency, and the number of co-signers.
3. **Given** the person has balances hidden in the app, **When** the list is shown, **Then** balances on the cards are hidden the same way.
4. **Given** a balance is still loading or cannot be fetched, **When** the card is shown, **Then** the rest of the card appears at once, the balance shows a loading placeholder and then either the amount or a plain "unavailable" mark; the card can still be opened.
5. **Given** a shared wallet has pending payments, **When** its card is shown, **Then** one status line states the most urgent condition, in this order: needs your signature, ready to broadcast, live room open, waiting for others.
6. **Given** a shared wallet has never been backed up, **When** its card is shown, **Then** it carries a "Not backed up" warning with icon and text.
7. **Given** this device holds no signing key for a shared wallet, **When** its card is shown, **Then** it is marked "View only".
8. **Given** several shared wallets, **When** the list is shown, **Then** those needing the person's signature or ready to broadcast come first, then the rest by most recent activity; the order does not change while the person is looking at the list.
9. **Given** the list, **When** the person wants to add a shared wallet, **Then** "Create" and "Import" are both visible as labelled actions, and with no shared wallets yet the empty state explains in one sentence what a shared wallet is and offers both.
10. **Given** drafts or unassigned shared wallets exist, **When** the list is shown, **Then** they appear in their own labelled sections as today, in the new card style.
11. **Given** a card, **When** the person wants to rename the shared wallet, **Then** rename is still reachable from the list within two taps.

---

### User Story 5 - Get back to my shared wallet in one tap (Priority: P3)

From the personal wallet's home, the Multisig shortcut shows how many payments across this wallet's shared wallets are waiting for the person. Tapping it reopens the shared wallet they used last; if they have only one, it opens that one directly; if they have none or never opened one, it opens the list.

**Why this priority**: It shortens a three-tap return to one, but the switcher in Story 1 already gives a way back, so this is an added convenience.

**Independent Test**: Open a shared wallet, leave to the personal home, tap the shortcut and confirm the same shared wallet opens; repeat with zero, one and several shared wallets.

**Acceptance Scenarios**:

1. **Given** payments in this wallet's shared wallets need the person, **When** they view any personal-wallet screen, **Then** the More tab shows the total count, the Multisig row inside the More list shows the same count, and on the home the Multisig shortcut shows it too; with none, no count is shown anywhere.
2. **Given** the person opened a shared wallet earlier on this device, **When** they tap the shortcut, **Then** that shared wallet's dashboard opens.
3. **Given** the last-used shared wallet was removed or belongs to a different personal wallet than the active one, **When** they tap the shortcut, **Then** the list opens instead.
4. **Given** the person has exactly one shared wallet in the active wallet, **When** they tap the shortcut, **Then** it opens directly.
5. **Given** More → Multisig, **When** the person taps it, **Then** it always opens the list, so the list stays reachable.

---

### Edge Cases

- A shared wallet name or personal wallet name is very long or uses non-Latin characters: the context line and the menu cue shorten in the middle or at the end and the full name is available in the switcher.
- The same shared wallet is owned by two personal wallets on the device: the context line names the active one.
- The person switches the active personal wallet while a shared wallet is open: the shared context ends and they land on that wallet's home, never on another wallet's shared wallet.
- A room is opened for a shared wallet that is not on this device yet (joined by code): the context line appears only once the payment has been matched to a shared wallet; the join screen before that shows the personal menu, and the room itself shows no menu (FR-028).
- A scan made from the shared menu is a DigiByte address or payment request: the shared wallet's send screen opens with it filled in (FR-002a). If this device cannot create payments for that shared wallet, the person is told so and offered to pay from the personal wallet instead.
- A scan made from the shared menu is a Digi-ID login: it is routed as today, and the person is told they are leaving the shared wallet.
- A scanned proposal belongs to a different shared wallet: the offer to open that wallet, as today.
- The Pending count or the More count is higher than 99: it shows "99+".
- The personal wallet is locked or view only: the More count shows what can be worked out from saved payments, and shows nothing rather than a wrong number when it cannot be worked out.
- A view-only copy: no sign, room or "Get signatures" actions; the card and the room explain why in one line.
- A payment is both in a live room and needs the person's signature: the card shows the one status "Live room" with a second line "Your signature is needed", sits in the "needs you" group, and its main button is Open room.
- Many pending payments (20 or more): the grouped list stays responsive and cancelled items remain folded.
- The room's time runs out while the review is open: the review closes with an explanation and the result screen is shown; a signature already made is kept.
- Reduced-motion preference: tracker, sticky button and messages appear without animation.
- Large text sizes: the five menu labels and the card's main button remain readable without overlapping.
- Balance cannot be loaded for any shared wallet (offline): the list still shows status lines from what is saved on the device.

## Requirements *(mandatory)*

### Functional Requirements

**Context and navigation**

- **FR-001**: Every screen that belongs to one shared wallet (dashboard, send, pending, a pending payment's own screen, activity, and a signing room matched to that shared wallet) MUST show a context line with the shared wallet's name, its scheme, and the name and colour of the personal wallet it belongs to.
- **FR-002**: While such a screen is open (except a signing room, see FR-028), the bottom menu MUST keep its five positions and present, in order: Overview, Activity, Scan, Pending, More, each acting on that shared wallet (More behaves as it does everywhere; Scan as in FR-002a).
- **FR-002a**: A DigiByte address or payment request scanned with the menu's Scan while a shared wallet is open MUST open that shared wallet's send screen with the scanned recipient, and amount when given, filled in, and with the context line showing which wallet is paying. It MUST NOT open the personal wallet's send screen unless the person chooses that. Every other kind of code (Digi-ID login, multisig proposal, room invite, redeem script) MUST be routed as it is today.
- **FR-003**: The bottom menu MUST be visibly distinguishable in shared-wallet mode from personal mode by more than colour alone, and its accessible name MUST state which shared wallet it belongs to.
- **FR-004**: Pending in the menu MUST show the count of that shared wallet's payments that need the person (their signature, ready to broadcast, or a failed broadcast to retry: the same payments as the "needs you" group in FR-018), capped at "99+", and no count when there are none.
- **FR-005**: The context line MUST open a switcher offering "Back to *personal wallet*" and the other shared wallets of that personal wallet, each with scheme and attention count.
- **FR-006**: Screens not tied to one shared wallet (list, create, import, guide, and the join screen before a match) MUST keep the personal menu.
- **FR-007**: The dashboard's action buttons MUST NOT repeat a destination offered by the menu under the same name; Send and Receive remain as the dashboard's own actions.
- **FR-008**: Changing the active personal wallet MUST end shared-wallet mode.
- **FR-009**: The Multisig shortcut on the personal wallet's home MUST show the total count of payments needing the person across that wallet's shared wallets, and MUST open the last-used shared wallet of the active wallet when one exists and is still available, the only shared wallet when there is exactly one, and otherwise the list.
- **FR-010**: More → Multisig MUST always open the list.
- **FR-010a**: In the personal menu, the More tab and the Multisig row inside the More list MUST show the same total count as the home shortcut (FR-009), capped at "99+", with an accessible label that says what is waiting (for example "2 multisig payments need you"), and no count when there are none. The count MUST be worked out from what is already saved on the device and MUST update when a payment is signed, sent, cancelled or added, without reopening the app.

**Shared wallet list**

- **FR-011**: The list heading MUST name the personal wallet whose shared wallets are shown.
- **FR-012**: Each card MUST show name, scheme, number of co-signers, balance in DGB and in the person's currency, and at most one status line chosen in this order of urgency: needs your signature, ready to broadcast, live room open, waiting for others. "Live room open" is shown only for a room this device has joined; the wallet cannot know about rooms it is not in.
- **FR-013**: Card balances MUST follow the app's hide-balance setting, MUST appear without delaying the rest of the card, and MUST show a plain "unavailable" mark when they cannot be loaded.
- **FR-014**: A card MUST show "Not backed up" when the shared wallet has never been backed up, and "View only" when this device cannot sign for it; both with icon and text.
- **FR-015**: Cards MUST be ordered with those needing the person first, then by most recent activity, and MUST NOT reorder while the list is being viewed.
- **FR-016**: Create and Import MUST both be visible, labelled actions on the list, including in the empty state.
- **FR-017**: Drafts, unassigned shared wallets and rename MUST remain available from the list.

**Pending**

- **FR-018**: Pending MUST group payments under plain headings in the order: needs you (including a failed broadcast awaiting retry), waiting for others, can't go through, cancelled; empty groups MUST be omitted and the cancelled group MUST be folded by default. An open payment known to be unable to complete (FR-025) belongs to "can't go through" and to no other group.
- **FR-018a**: A payment that has been sent MUST NOT appear in Pending. Sending MUST show a confirmation with the transaction ID and a link to the payment in the shared wallet's Activity.
- **FR-019**: Each open payment card MUST show one status, amount, shortened recipient, signature progress with which co-signers have signed, and age, and MUST offer exactly one main button fitting its state.
- **FR-019a**: The Pending list MUST NOT sign, send or retry a payment by itself. A card's main button MUST open the payment's own screen (or its live room); signing, the slide-to-broadcast control and retrying a failed broadcast MUST be offered only on the payment's own screen and in the signing room, with the full payment summary in view.
- **FR-020**: All other actions available today (sign together online, share with co-signers, add a signed copy, cancel, remove a cancelled item) MUST be offered on the payment's own screen, reachable within two taps from the card, each with a one-line explanation, with destructive actions visually separated and confirmed as today.
- **FR-021**: Each pending payment MUST have its own screen, opened by tapping its card or the card's main button, showing status, the full payment summary used elsewhere (full address, fee, total leaving the wallet, note, details), who has signed, and the main button. Back MUST return to Pending. The screen MUST be openable directly for a given payment (after a scan, from a room's result screen), and MUST show a plain "no longer here" message with a way back to Pending when that payment has been removed. "Clear all cancelled" stays on the Pending list.
- **FR-022**: Adding a proposal (scan, paste, file) and joining a room with a code MUST be offered from one "Add a proposal" entry at the top of Pending, and inline in the empty state.
- **FR-023**: Share panels, QR codes and import forms MUST appear on the payment's own screen and MUST NOT expand inside the Pending list.
- **FR-024**: Results of actions MUST be reported by brief messages that do not displace the list; failures MUST remain until dismissed.
- **FR-025**: A payment that can no longer complete MUST say why in plain words; its card button MUST open the payment's own screen, where Cancel is the main action. Once this device knows a payment cannot complete, it MUST NOT be counted as needing the person (FR-004, FR-009, FR-010a) and MUST NOT supply the status line of its shared wallet's card (FR-012). That knowledge comes from the checks the Pending list and the payment's own screen already make; it is kept only while the app is open, and the counts themselves still make no network call.

**Signing room**

- **FR-026**: The room MUST present the payment once, in one summary, with amount, recipient and total leaving the wallet visible by default and the rest behind a details control.
- **FR-027**: The room MUST show a three-step tracker (Invite, Sign, Broadcast) reflecting the room's current state, readable as text by assistive technology.
- **FR-028**: The bottom menu MUST be hidden from the moment the person is in a signing room until they leave its result screen. The viewer's one main button MUST be pinned to the bottom edge, remain visible while scrolling, and MUST NOT be covered by the device's system bars. Back and the room menu MUST remain available as the ways out. The join screen keeps the bottom menu.
- **FR-029**: The invite (code, share action, copy link, copy code, QR code) MUST be placed directly under the headline for the host while fewer co-signers than the threshold have joined or signed, and folded into one row otherwise.
- **FR-030**: The signer list MUST show "X of Y signatures" and how many more are needed, list the viewer first and then others ordered signed, online, offline, and keep presence and signing status distinct with icon and text.
- **FR-031**: Time left and connection state MUST be visible at the top of the room without scrolling; at 5 minutes or less the time MUST be emphasised and announced once.
- **FR-032**: Leave and Close room MUST be offered from a labelled room menu, each stating its consequence; Close room MUST be host-only and confirmed.
- **FR-033**: When a room ends, a result screen MUST state the outcome, the signatures kept, the transaction ID with a copy action when sent, and one next action.
- **FR-034**: The join screen MUST accept the code by typing, single paste or scan, state which wallet the person is joining as, and enable joining only for a complete code.

**Across all four areas**

- **FR-035**: All states MUST use the shared vocabulary and icons already defined for multisig (needs your signature, you signed, waiting for others, ready to broadcast, live room, sent, cancelled, failed) and MUST never rely on colour alone. One word is used for the last step everywhere: the state is "Ready to broadcast", its controls are "Review & broadcast", "Slide to broadcast" and "Broadcast", the tracker's third step is "Broadcast", and a finished payment is "Sent".
- **FR-036**: All new and changed screens MUST work in light and dark themes, at phone width, with touch targets of at least 44 px, full keyboard operation, announced status changes, and motion suppressed under a reduced-motion preference.
- **FR-037**: Every new or changed label of the bottom menu (Overview, Pending, the counts and their accessible labels, including the count on More and on the Multisig row) MUST be provided in English and Spanish and MUST fall back to English, never to a raw key, when a translation is missing. Text on the multisig screens themselves stays English only, as today.
- **FR-038**: This feature MUST NOT change signing, signature checks, room rules or lifetimes, what the signing service receives, what is stored about payments and keys, or who can open what. The only new saved item is the last-used shared wallet per personal wallet, kept on the device.
- **FR-039**: Every action available on these screens today MUST still be available after the change.

### Key Entities

- **Shared wallet (multisig)**: A wallet that needs several co-signers to spend. Has a name, a scheme (signatures required of co-signers total), co-signers, a balance, a backup state, and belongs to one or more personal wallets on the device.
- **Personal wallet**: One of the person's own wallets; the owner context of a shared wallet, with a name and colour.
- **Shared-wallet context**: The state of "being inside" one shared wallet. Decides the context line and what the bottom menu shows. Begins when a shared wallet's screen opens and ends when the person leaves or switches personal wallet.
- **Pending payment**: A proposed payment from a shared wallet, with amount, recipient, fee, note, signatures collected, and a state (needs your signature, waiting for others, ready to broadcast, live room, sent, cancelled, failed, cannot complete).
- **Attention count**: The number of pending payments that need the person (their signature, ready to broadcast, or a failed broadcast to retry), per shared wallet and in total per personal wallet.
- **Signing room**: A timed live session for one pending payment, with an invite code, a host, participants with presence and signing status, time left, and a stage (Invite, Sign, Broadcast, ended).
- **Last-used shared wallet**: Per personal wallet, the shared wallet the person opened most recently on this device.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From any screen inside a shared wallet, 100% of bottom-menu taps on Overview, Activity and Pending keep the person inside that shared wallet.
- **SC-002**: In a moderated test, at least 90% of participants shown a shared wallet screen for 5 seconds can name both the shared wallet and the personal wallet it belongs to.
- **SC-003**: Returning from the personal wallet's home to the last-used shared wallet takes 1 tap (down from 3), and leaving a shared wallet for the personal wallet takes at most 2 taps.
- **SC-004**: No screen in the shared wallet shows two controls with the same name that lead to different places.
- **SC-005**: On Pending, a person with a payment waiting for their signature reaches the review in 1 tap from opening the screen, without scrolling, on a standard phone.
- **SC-006**: Every open payment card shows exactly one main button; in a moderated test at least 90% of participants pick the correct next action for each of five states on the first try.
- **SC-007**: In a signing room, at least 90% of first-time participants state correctly within 10 seconds what the group is waiting for and what they should do.
- **SC-008**: A host can share a room invite within 10 seconds of the room opening, without scrolling.
- **SC-009**: A joining co-signer goes from opening an invite link to a confirmed signature in under 45 seconds, excluding unlock time.
- **SC-010**: From the list alone, at least 90% of participants correctly identify which shared wallets need their signature, which are not backed up and which are view only.
- **SC-011**: The list shows every card's name, scheme and status within 1 second of opening; balances fill in afterwards without moving the cards.
- **SC-012**: Every action available before the change can still be completed after it, verified against a checklist of existing actions; no signing or security behaviour differs.
- **SC-013**: All changed screens pass an accessibility review: no colour-only meaning, status changes announced, keyboard operable, and readable contrast in both themes.

## Assumptions

- Shared wallets remain scoped to the active personal wallet, as today. Showing shared wallets of other personal wallets on the device in one combined list is out of scope.
- The bottom menu changes only what its positions point to; its shape, the raised Scan button and the More list are unchanged. Request is not offered inside a shared wallet because receiving there is done with the dashboard's Receive.
- The signing room is a focused task: the bottom menu is hidden there (FR-028) and returns as soon as the person leaves the room.
- The app's existing word "broadcast" is kept for the state and its controls (FR-035) instead of introducing "send" next to it.
- Balances on list cards come from the same source the dashboard uses today, and the person's currency and hide-balance setting are the app's existing ones.
- "Most recent activity" for ordering uses what is already saved on the device (latest pending or activity item); no new tracking is introduced.
- The earlier decision that proposal receive options are always visible on Pending (spec 022) is replaced by one always-visible "Add a proposal" entry that opens them; the options themselves are unchanged.
- The state names, icons, payment summary, signer cards, headline card and connection notice introduced by spec 018 are reused and rearranged, not redefined.
- Room rules, lifetimes, invite codes, who may join, and what the service sees are exactly as defined in spec 017.
- Creating and importing shared wallets, the creation room, the guide, and the send form are out of scope apart from showing the context line and shared menu where FR-001 and FR-002 apply, and the send form accepting a scanned recipient and amount (FR-002a).
- Translating the multisig screens into Spanish is out of scope; it is better done once for all multisig screens.
- Moderated-test criteria are checked with a small group (five or more people) before release; the remaining criteria are checked by inspection and walkthrough.
