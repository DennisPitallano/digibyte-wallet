# Feature Specification: First-Receive Flow

**Feature Branch**: `025-first-receive-flow`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "First-receive flow: make a new user's first received payment the centre of onboarding on the wallet's Home page. (1) Remove the 5-slide OnboardingWalkthrough modal shown on first Home; replace it with at most one line of plain copy in the empty state. (2) Home's empty transaction state shows the wallet's receiving QR code and a Copy address button (plus a link to the full Receive page) instead of a text-only card, for every wallet type that can receive, including watch-only. (3) Celebrate the first received payment: the first incoming transaction a wallet ever sees (pending or confirmed) triggers a one-time celebration comparable to the existing Send success overlay, showing the amount; it is remembered per wallet so it never repeats, and it must not fire for recovered, imported or watch-only wallets that already have history when first loaded. It respects reduced-motion and the Baku mascot setting. (4) Reduce first-session clutter: while a wallet has no transactions, the PWA install banner and the sponsors strip stay hidden on Home; they return after the first payment. The Getting started checklist stays. Out of scope: changes to wallet creation, backup, key handling, storage of secrets, notifications or push, localisation beyond using the existing translation mechanism for new strings (English and Spanish). No signing, network or server rule changes."

## Context

A person who has just created a wallet arrives on Home with nothing in it. Today that first screen is covered by a five-slide introduction, and behind it sit a mascot message, a getting-started checklist, an install banner, a sponsors strip and a card that says "No transactions yet" with no button. Nothing on the screen helps with the one thing a new wallet needs: getting a first payment into it. And when that payment does arrive, the wallet shows a small toast, while sending money gets a full celebration.

This feature makes receiving the first payment the purpose of the first session. The introduction slides go away, the empty Home shows the address and QR code to share, the screen is quieter until there is something in the wallet, and the first payment is marked as the moment it is.

## Clarifications

### Session 2026-11-22

- Q: Where should the first-payment celebration appear: on whichever wallet page the user is on when the payment is detected, or only on Home? → A: On any page that shows the wallet's balance or activity (Home, Receive, Activity), wherever the payment is first seen.
- Q: Should the celebration offer one suggested next step besides closing it? → A: Yes. It offers **Install app** when the wallet is not installed; when it is already installed, only the close action is shown.
- Q: When a device has several wallets, should the install banner and sponsors strip be hidden on Home whenever the active wallet is empty, or only when no wallet on the device has ever had a payment? → A: Only while no wallet on this device has a transaction. Once any wallet has one, both items follow their normal rules for every wallet.
- Q: Should the one line of copy in the empty Home be spoken by the Baku mascot, as the empty Home's message is today, or be plain text with no mascot? → A: The existing Baku message is the one line, reworded to point at the address and QR code. It follows the existing Baku rules, so it is not shown when the mascot is Off or Guardian only, or after the user dismisses it; the empty state must be understandable without it.
- Q: Should the Activity page's empty state also point a new user to receiving their first payment, or stay as it is? → A: It gains a single **Receive** button that opens the Receive page. No QR code is shown on Activity.
- Q: Should a wallet that was created on this device count as empty from the moment it is created, instead of waiting for its first successful history load? → A: Yes. A wallet created on this device is recorded as empty at creation; recovered, imported and watch-only wallets still wait for their first complete history load.
- Q: Should the reworded mascot message on the empty Home be translated into Spanish, or stay English-only like every other mascot message today? → A: It stays English-only. Mascot messages are exempt from the Spanish requirement; every other new string is in English and Spanish.
- Q: Should the Receive page check for incoming payments by itself while it is open, the way Home does? → A: Yes, Receive only, using the same refresh as Home. Activity is unchanged (opened or pulled to refresh). The "no new network requests" rule is narrowed to no new kinds of request and no new destinations.
- Q: During a new user's first session, should the install banner be hidden on every page inside the wallet, or only on Home? → A: On every page inside the wallet, until some wallet has a transaction. It is unchanged on the setup pages (Welcome, Recover, Watch-only import), and the Install app entry in Settings is unchanged. (Analysis follow-up: "inside the wallet" means every page other than those three, so Create wallet, Unlock, Help and About hide it too.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Share an address straight from an empty Home (Priority: P1)

A person finishes creating a wallet and lands on Home. No introduction covers the screen. Where the transaction list will be, they see a QR code for their receiving address, the address itself, a **Copy address** button and a link to the full Receive page, with the mascot's one short message saying that this is how to get their first DGB. They copy the address or let someone scan the code without leaving Home.

**Why this priority**: This is the step every new user must take before the wallet is of any use, and today it needs the user to dismiss a modal and then find Receive on their own. It delivers value with no other story built.

**Independent Test**: Create a new wallet, arrive on Home, and confirm that with no dismissals the QR code, the address and Copy address are visible, that Copy address puts the full receiving address on the clipboard, and that the link opens the Receive page.

**Acceptance Scenarios**:

1. **Given** a wallet that has just been created, **When** Home opens for the first time, **Then** no introduction slides or other full-screen overlay appear.
2. **Given** a wallet whose history is known to be empty, **When** Home is showing, **Then** the transaction area shows the receiving address as a QR code and as text, a Copy address button and a link to the Receive page, and the mascot's message above it points at the address and QR code as the way to get a first DGB.
3. **Given** the empty state is showing, **When** the user taps Copy address, **Then** the full receiving address is copied and the wallet confirms it was copied.
4. **Given** the empty state is showing, **When** the user taps the Receive link, **Then** the Receive page opens showing the same address.
5. **Given** a watch-only wallet with no history, **When** Home is showing, **Then** the same QR code, address and Copy address appear, because a watch-only wallet can still receive.
6. **Given** the device is offline and the wallet has no history saved, **When** Home is showing, **Then** the QR code and address are still shown, because they do not depend on the network.
7. **Given** the wallet's history could not be loaded and nothing is saved from before, **When** Home is showing, **Then** the existing "Unable to load data" state with **Try again** is shown, with the QR code, address, Copy address and Receive link beneath it and without the mascot's message.
8. **Given** the mascot is set to Off or Guardian only, or the user has dismissed the message, **When** the empty state is showing, **Then** no explanatory line is shown and the QR code, address, Copy address and Receive link are all still present and clearly labelled.
9. **Given** a wallet whose history is known to be empty, **When** the user opens Activity, **Then** its "No transactions yet" state shows a **Receive** button that opens the Receive page.
10. **Given** a wallet that has transactions, **When** a search or filter on Activity matches nothing, **Then** the Receive button is not shown and the existing "nothing matches" state is unchanged.

---

### User Story 2 - The first payment is celebrated once (Priority: P1)

A new user shares their address and a payment arrives. The first time a wallet page (Home, Receive or Activity) shows that payment, whether the wallet was open when it arrived or is opened later, a celebration appears with the amount received and a short congratulation. They close it and stay on the page they were on. It never appears again for that wallet.

**Why this priority**: The first received payment is the point at which a new user has succeeded with the wallet. Marking it is the main retention goal of this feature and is what the empty-state work in Story 1 leads to.

**Independent Test**: With a new, empty wallet, send it a payment from another wallet. Confirm the celebration appears once with the right amount, closes on request, and does not appear again after further payments, a reload, or locking and unlocking.

**Acceptance Scenarios**:

1. **Given** a wallet that was seen empty on this device, **When** its first incoming payment is detected while Home, Receive or Activity is open, **Then** a celebration appears on that page showing the amount received.
2. **Given** a wallet that was seen empty on this device and then closed, **When** the user next opens Home, Receive or Activity after a payment arrived, **Then** the celebration appears once that page shows the payment.
3. **Given** the first incoming payment is still unconfirmed, **When** it is detected, **Then** the celebration appears and says the payment is on its way rather than complete.
4. **Given** the celebration is showing, **When** the user closes it, **Then** they stay on the page they were on, and the payment is in the wallet's transaction list.
5. **Given** the celebration has been shown for a wallet, **When** any later payment arrives, **Then** no celebration appears and the usual "payment received" notice is shown instead.
6. **Given** more than one incoming payment is detected at the same time as the first, **When** the celebration appears, **Then** it appears once and shows the total received.
7. **Given** the user has hidden balances, **When** the celebration appears, **Then** it does not reveal the amount.
8. **Given** the device asks for reduced motion, **When** the celebration appears, **Then** it shows without moving decoration and with the same text and actions.
9. **Given** the mascot is set to Off or Guardian only, **When** the celebration appears, **Then** no mascot image or mascot line is shown, and the celebration's own heading and amount still appear.
10. **Given** the wallet is not installed as an app, **When** the celebration appears, **Then** it offers **Install app** as well as the close action.
11. **Given** the celebration offers Install app, **When** the user taps it, **Then** the celebration closes and the same install flow as the Install app entry in Settings starts (the device's install prompt where the browser has one, otherwise the step-by-step instructions).
12. **Given** the wallet is already running as an installed app, **When** the celebration appears, **Then** only the close action is shown.
13. **Given** a wallet just created on this device whose history has not yet loaded successfully (the device was offline or the load failed), **When** a payment arrives and the first successful load already contains it, **Then** the celebration appears.
14. **Given** a new user has the Receive page open showing their QR code, **When** a payment to the wallet arrives, **Then** Receive finds it without the user doing anything and the celebration appears there.
15. **Given** Activity is open when the first payment arrives, **When** the user pulls to refresh or reopens Activity, **Then** the celebration appears; Activity does not check by itself.

---

### User Story 3 - No celebration for a wallet that already has history (Priority: P1)

A person recovers a wallet from a recovery phrase, imports a private key, or adds a watch-only wallet that has been used before. When its history loads for the first time on this device, the wallet shows the balance and list as normal. It does not congratulate them on a "first payment" that happened long ago, now or at any later time.

**Why this priority**: A false celebration on a recovered wallet reads as a bug and undermines trust in the figures on screen. Story 2 must not ship without it.

**Independent Test**: Recover a wallet that has past transactions, wait for history to load, and confirm no celebration appears then or when a new payment arrives later.

**Acceptance Scenarios**:

1. **Given** a recovered, imported or watch-only wallet whose first successful history load on this device contains transactions, **When** that load completes, **Then** no celebration appears.
2. **Given** such a wallet, **When** a new payment arrives later, **Then** no celebration appears.
3. **Given** a recovered wallet that the wallet has confirmed is empty (a complete load with nothing in it, after it has finished finding which addresses have been used), **When** its first payment later arrives, **Then** the celebration appears, as for a new wallet.
4. **Given** a wallet that existed before this feature was released and already has transactions, **When** the user opens the updated wallet, **Then** no celebration appears.
5. **Given** a recovered, imported or watch-only wallet whose history has never loaded successfully on this device, **When** a load fails or is only partial, **Then** the wallet is not treated as empty and no decision about the celebration is made from that load.

---

### User Story 4 - A quieter wallet until it has something in it (Priority: P2)

While no wallet on the device has a transaction, the install banner is not shown on any page inside the wallet and Home does not show the sponsors strip, so the address to share is the clear focus on Home and on Receive. After the first payment to any wallet both return and behave as they do today, for every wallet. The getting-started checklist is shown throughout.

**Why this priority**: It supports Story 1 by removing competition for attention, but Stories 1 to 3 are useful without it.

**Independent Test**: With an empty wallet, open Home, Receive and Activity and confirm the install banner is on none of them, that Home shows no sponsors strip, and that the checklist is shown. Receive a payment and confirm both return.

**Acceptance Scenarios**:

1. **Given** no wallet on this device is known to have a transaction, **When** any page other than Welcome, Recover or Watch-only import is showing, **Then** the install banner is not shown, and Home does not show the sponsors strip.
2. **Given** at least one wallet on this device is known to have a transaction, **When** any page inside the wallet is showing for any wallet, including an empty one, **Then** the install banner and the sponsors strip follow their existing rules.
3. **Given** it is not yet known whether any wallet on this device has a transaction, **When** a page is still loading, **Then** neither item is shown, so nothing appears and then disappears.
4. **Given** an empty wallet, **When** the user opens Settings, **Then** the Install app entry is available as it is today.
5. **Given** no wallet exists yet or one is being set up, **When** the user is on Welcome, Recover or Watch-only import, **Then** the install banner behaves there as it does today.
6. **Given** an empty wallet, **When** Home is showing, **Then** the getting-started checklist is shown under its existing rules.
7. **Given** a user with a funded wallet adds a second, empty wallet, **When** they switch between the two, **Then** the install banner and the sponsors strip do not appear or disappear because of the switch.

---

### Edge Cases

- **Payment from the user's own other wallet**: counts as a first received payment for the receiving wallet.
- **Several wallets on one device**: the celebration is decided separately for each wallet. The quieter wallet is decided for the device as a whole: it ends when any wallet has a transaction.
- **The only wallet with transactions is deleted**: the quieter wallet does not come back; once it has ended on a device it stays ended.
- **First transaction is not a DGB receipt** (for example a transaction that moves no DGB to the wallet): no celebration; the wallet is then treated as having history, and no celebration appears later.
- **The first payment is dropped before confirming**: the celebration has already been shown and is not shown again when another payment arrives.
- **Wallet deleted and created or recovered again**: the record that the celebration was shown is removed with the wallet, so the wallet is judged afresh by Story 3's rule.
- **A recovered, imported or watch-only wallet is paid before the wallet has confirmed it is empty** (in the first moments after it is added, or while its addresses cannot be checked): the first load that shows the payment records the wallet as already having history, and there is no celebration. This is accepted: a missed celebration on a recovered wallet costs less than a false one.
- **Same wallet on a second device**: each device decides for itself. A wallet that already has history when first loaded on the second device gets no celebration there.
- **The wallet locks, or the user leaves the page, while the celebration is showing**: it closes and is not shown again.
- **The first payment is detected while the user is on a page that shows no balance or activity** (for example Settings): the celebration appears when they next open Home, Receive or Activity.
- **Another dialog is already open when the payment is detected** (for example the "What's New" window after an update): the celebration waits until that dialog is closed.
- **Users who had not finished the old introduction slides**: the slides are simply never shown again; nothing replaces them.
- **Receive is left open for a long time**: the automatic check keeps Home's pacing, so it adds no more load on the data sources than leaving Home open does.
- **The network is down while Receive is open**: the QR code and address stay as they are; checks resume when the network returns.
- **Test networks**: the same behaviour on testnet as on the main network. On a developer's local regtest network the wallet does not read chain history, so the "already has history" check cannot work there and a recovered wallet may be celebrated; this is accepted for a developer-only network.

## Requirements *(mandatory)*

### Functional Requirements

**Removing the introduction**

- **FR-001**: The wallet MUST NOT show the multi-slide introduction on Home, for new or existing users.
- **FR-002**: Nothing MUST be added to the first Home in its place; the only explanatory copy is the mascot message described in FR-005.

**Empty Home**

- **FR-003**: When the active wallet's history is known to be empty, Home's transaction area MUST show the wallet's current receiving address as a QR code and as text, a Copy address control, and a link to the Receive page.
- **FR-004**: The address shown in the empty state MUST be the same address the Receive page and the balance card show for that wallet at that moment.
- **FR-005**: The empty state's only explanatory copy MUST be the existing mascot message for an empty Home, reworded in plain words with no technical terms to point at the address and QR code. It MUST follow the existing mascot rules (not shown when the mascot is Off or Guardian only, or once dismissed), and the empty state MUST be understandable without it through the labels on its controls.
- **FR-006**: FR-003 MUST apply to every wallet type that can receive, including watch-only wallets and wallets imported from a private key.
- **FR-007**: The empty state MUST work with no network connection.
- **FR-008**: When history could not be loaded and no earlier figures are saved, Home MUST show the existing "Unable to load data" state with its **Try again** action. The address, QR code, Copy address and Receive link MUST still be shown beneath it (FR-007), without the mascot's message, because nothing is known yet about the wallet's history.
- **FR-009**: The empty state MUST be replaced by the transaction list as soon as the wallet has a transaction to show.
- **FR-010**: When the active wallet's history is known to be empty, Activity's "No transactions yet" state MUST show a Receive action that opens the Receive page, and MUST NOT show a QR code. The action MUST NOT appear when the list is empty only because of a search or filter.

**First-payment celebration**

- **FR-011**: The wallet MUST record, per wallet on this device, whether the wallet started empty or already had history. A wallet created on this device from a newly generated recovery phrase MUST be recorded as empty at creation, without waiting for the network. Every other wallet (recovered, imported or watch-only) MUST be recorded from the outcome of its first successful and complete history load, with two refinements: history already saved on this device that contains a transaction is enough to record the wallet as already having history, and a wallet MUST NOT be recorded as empty until the wallet has finished finding which of its addresses have been used.
- **FR-012**: A wallet recorded as already having history MUST never show the celebration.
- **FR-013**: A wallet recorded as empty MUST show the celebration the first time Home, Receive or Activity shows an incoming DGB payment for it, whether that payment is unconfirmed or confirmed, on whichever of those pages sees it first.
- **FR-014**: The celebration MUST be shown at most once per wallet on a device, and the fact that it was shown MUST survive reloads, locking and app updates.
- **FR-015**: A failed or partial history load MUST NOT be used to record a recovered, imported or watch-only wallet as empty, and MUST NOT trigger the celebration for any wallet. Whether a load was complete MUST be judged from that load alone, so that two loads running at the same time cannot be mistaken for each other.
- **FR-016**: The celebration MUST show the amount received in DGB, and the fiat value when a price is available, using the user's display settings. When several incoming payments are detected together it MUST show their total.
- **FR-017**: When balances are hidden, the celebration MUST NOT show the amount.
- **FR-018**: When the payment is unconfirmed, the celebration's wording MUST say it is on its way, not that it is complete.
- **FR-019**: The celebration MUST have a clear way to close it, MUST be usable with a keyboard and a screen reader, and MUST be announced to assistive technology when it appears.
- **FR-020**: The celebration MUST honour the device's reduced-motion setting by omitting moving decoration.
- **FR-021**: The celebration MUST honour the mascot setting: no mascot image or mascot line when it is Off or Guardian only. The celebration's own heading and amount, which are not mascot text, MUST always be shown.
- **FR-022**: When the celebration is shown, the ordinary "payment received" notice for the same payment MUST NOT also be shown.
- **FR-023**: The celebration MUST NOT appear over another open dialog; it MUST wait until that dialog is closed.
- **FR-024**: While the Receive page is open, the wallet MUST check for new incoming payments by itself, using the same automatic refresh as Home (same data sources, same pacing, same limits on how many addresses are read). Activity MUST NOT gain an automatic check.
- **FR-025**: A failed automatic check on Receive MUST NOT change or hide anything on the page, MUST NOT show an error, and MUST be retried at the next interval.
- **FR-026**: When Receive's automatic check finds an incoming payment that is not a wallet's first, the wallet MUST show the same "payment received" notice Home shows, once per payment.
- **FR-027**: When the wallet is not installed as an app, the celebration MUST offer an **Install app** action that starts the same install flow as the Install app entry in Settings; when the wallet is installed, the celebration MUST NOT show that action. Declining or ignoring it MUST have no other effect.
- **FR-028**: Deleting a wallet MUST remove its first-load record and its celebration record.

**Quieter wallet**

- **FR-029**: The install banner, on every page except the three setup pages named in FR-030, and the sponsors strip on Home MUST be shown only once at least one wallet on this device is known to have a transaction; from then on their existing rules apply unchanged for every wallet, and this MUST NOT be reversed by switching, adding or deleting wallets.
- **FR-030**: FR-029 MUST NOT change the install banner on the setup pages where it always shows today (Welcome, Recover, Watch-only import), or the Install app entry in Settings. Every other page, including Create wallet, Unlock, Help and About, follows FR-029.
- **FR-031**: The getting-started checklist MUST keep its existing behaviour.

**Language and safety**

- **FR-032**: All new text MUST be provided in English and Spanish through the wallet's existing translation mechanism, except text spoken by the mascot (the empty Home message in FR-005 and any mascot line in the celebration), which stays English-only like every other mascot message.
- **FR-033**: The feature MUST NOT add any new kind of network request or any new destination; the only added traffic is Receive's automatic check (FR-024), which makes the same requests Home's refresh already makes. The feature MUST NOT send anything else about the wallet (that a first payment occurred, or that a celebration was shown) off the device.
- **FR-034**: The feature MUST NOT change how wallets are created, backed up, unlocked or signed with, or how secrets are stored. The only addition at wallet creation is saving the non-secret first-load record required by FR-011.

### Key Entities

- **First-load record**: for one wallet on one device, whether it started empty or already had history: set at creation for a wallet created on this device, otherwise from saved history that contains a transaction or from its first successful, complete history load. Decides whether the wallet can ever show the celebration. Holds nothing secret.
- **First-session marker**: for one device, that some wallet on it has had a transaction. Ends the quieter wallet for good. Holds nothing secret.
- **Celebration record**: for one wallet on one device, that the first-payment celebration has been shown. Holds nothing secret and is removed with the wallet. Only a wallet that started empty can ever have one, so the first-load record and this one may be kept as a single per-wallet value.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person who has just created a wallet can copy their receiving address with one tap on Home, having dismissed nothing first.
- **SC-002**: On a new wallet's first Home, the number of items competing with the address (the introduction slides, the install banner and the sponsors strip) drops from three to zero; the balance card, the action buttons, the checklist and one mascot message remain.
- **SC-003**: In testing with new wallets, the celebration appears for 100% of first received payments and is shown exactly once per wallet.
- **SC-004**: In testing with recovered, imported and watch-only wallets that have history, the celebration appears in 0% of cases, including after later payments.
- **SC-005**: The celebration is on screen within 2 seconds of Home, Receive or Activity showing the first incoming payment.
- **SC-008**: With Receive open, a payment sent to the wallet is shown (celebration or notice) within one automatic refresh interval of the data source reporting it, with no action by the user.
- **SC-006**: With no network connection, the empty Home still shows the QR code and address in 100% of attempts.
- **SC-007**: Every new piece of text covered by FR-032 has a Spanish translation, and appears in Spanish when the wallet's saved language is Spanish.

## Assumptions

- "First payment" means the first incoming DGB payment to a wallet that this device knows started empty: one it created itself, or one whose first successful load here was empty. A recovered or imported wallet's history elsewhere is not known, so its first successful load on this device is the reference point.
- Wallets created on this device before this feature was released have no creation-time record, so they are judged by their first successful load after the update.
- A payment that arrives while the wallet is closed is celebrated the next time Home, Receive or Activity shows it.
- The celebration has no "view payment" action; after it closes the payment is visible in the wallet's list.
- Whether the wallet is installed is decided the same way as for the Install app entry in Settings.
- Wallets that are empty when the feature is released are eligible for the celebration; wallets with history are not.
- Receive's automatic check runs for every wallet, not only for wallets still waiting for a first payment, because someone waiting to be paid benefits whether or not it is their first time.
- The wallet has no language picker yet (Settings shows it as coming soon), so most users will see the new text in English. The Spanish translations are added now so they are ready when a picker exists; adding the picker is out of scope.
- The saved marker left by the old introduction slides is ignored; it does not need to be cleaned up.
- The existing Send success overlay is the reference for the look and weight of the celebration.
- Out of scope: wallet creation and backup steps, notifications or push, DigiDollar and multisig first-payment moments, translating existing text, making mascot messages translatable, and any server change.
