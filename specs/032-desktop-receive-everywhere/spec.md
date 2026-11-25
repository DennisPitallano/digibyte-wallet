# Feature Specification: Desktop Receive Everywhere, and Scanned Payments Kept Out of the Address Bar

**Feature Branch**: `feature/desktop-receive-everywhere` (spec directory `032-desktop-receive-everywhere`)

**Created**: 2026-11-25

**Status**: Approved

**Input**: Follow-up to spec 030 (`030-desktop-paste-scan`): "Bring the desktop treatment from spec 030 to the screens it did not cover." Screens named: DigiDollar Receive, Home's empty state, the shared-wallet Receive sheet, and optionally Share account xpub. Also decide whether addresses and `digibyte:` requests read by the menu's Paste / Scan should reach Send in memory, like payment links already do.

## Context

**What spec 030 changed.** On a wide window (1024px and wider), Receive and Request lead with the address and its buttons. The QR code follows, smaller, labelled "Scan with your phone". Phones keep the QR code on top. A payment link to this wallet, pasted or scanned in the menu, is handed to Send in memory, so Send's address stays `/send` and a reload shows an empty form.

**What it left out.**

- **DigiDollar Receive** still leads with a 192px QR code on every window size. On a desktop, the person almost always copies the address instead.
- **The shared-wallet Receive sheet** (opened from a shared wallet's Receive button) has the same QR-first order.
- **Home's empty state** is already a small side-by-side card: a 112px QR code next to the address and Copy. It does not say who the QR code is for.
- **A bare address or a `digibyte:` request** pasted or scanned in the menu still opens Send with `?address=…&amount=…` in the address bar. In an open shared wallet it opens `/multisig/{id}/send?address=…&amount=…`. A reload, a bookmark or the browser's history keeps that payment. Payment links (spec 030) already avoid this.

## Clarifications

### Session 2026-11-25

- Q: Which screens get spec 030's wide-screen order? → A: DigiDollar Receive and the shared-wallet Receive sheet. Home's empty state keeps its side-by-side card and only gains the "Scan with your phone" caption under its QR code on wide windows.
- Q: Do these screens get **Copy payment link**? → A: No. Spec 030's rule stays: a payment link is offered only where an amount is set. None of these screens sets an amount. DigiDollar addresses are not DigiByte payments, so the `/pay` page cannot carry them in any case. They offer Copy address only, as today.
- Q: Is Share account xpub (`/settings/xpub`) in scope? → A: No. It is not a payment screen. Its missing Spanish text is a separate follow-up.
- Q: Should addresses and `digibyte:` requests read by the menu's Paste / Scan reach Send in memory? → A: Yes, for the personal wallet and for an open shared wallet. Every payment the menu reads (pasted, dropped, read from a picture or the screen, or scanned with the camera) reaches Send without its address or amount in the address bar.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scanned and pasted payments stay out of the address bar (Priority: P1)

A person on a laptop copies an address from a chat and pastes it in the menu's Paste / Scan. Send opens with the address filled in. The browser's address bar shows only the Send page, never the address or the amount. Reloading Send shows an empty form. The same happens with a `digibyte:` request with an amount, with a camera scan on a phone, and inside an open shared wallet, where the shared wallet's Send opens instead.

**Why this priority**: It closes the one place where a payment the person read still ends up in the address bar (and so in history, bookmarks and reloads) after spec 030. It changes behaviour; the layout stories do not.

**Independent Test**: Paste an address, then a `digibyte:` request with an amount, in the menu's panel, with the personal wallet and with a shared wallet open. Each time, confirm the right Send opens with the values filled in, the address bar shows no address or amount, and a reload shows an empty form.

**Acceptance Scenarios**:

1. **Given** the personal wallet is open, **When** a bare DigiByte address is pasted or scanned in the menu, **Then** Send opens with the address filled in and the address bar shows no address.
2. **Given** the personal wallet is open, **When** a `digibyte:` request with an amount is read in the menu, **Then** Send opens with the address and the amount filled in, and neither appears in the address bar.
3. **Given** a shared wallet is open, **When** an address or a `digibyte:` request is read in the menu, **Then** that shared wallet's Send opens with the values filled in, and neither appears in the address bar.
4. **Given** Send (personal or shared) is already open, **When** another payment is read in the menu, **Then** the new address and amount replace the old ones, as a scan does today.
5. **Given** Send opened from a scanned payment, **When** the page is reloaded, **Then** the form is empty.
6. **Given** the shared wallet's Send cannot open (not found, or not this wallet's), **When** a payment was read for it, **Then** the existing **Pay from my wallet** button still offers the same payment from the personal wallet, again without putting it in the address bar.
7. **Given** a `digibyte:` request with a label or message, **When** it is read in the menu, **Then** only the address and amount reach Send; the note never reaches Send's memo.
8. **Given** a Digi-ID link, a room invite, a multisig proposal or a redeem script, **When** it is read in the menu, **Then** it goes where it goes today.

---

### User Story 2 - DigiDollar Receive leads with the address on wide screens (Priority: P2)

On a desktop-width window, DigiDollar Receive shows the network, the address and **Copy** / **Share** first. The QR code follows, smaller, with "Scan with your phone" under it. On a phone the page looks as it does today.

**Why this priority**: The same reasoning as spec 030's Receive: a desktop person copies; the QR code is for a phone next to them.

**Independent Test**: Open DigiDollar Receive at 1280px and at 390px wide. At 1280px the address and buttons come before the QR code and the caption shows. At 390px the QR code is on top and there is no caption.

**Acceptance Scenarios**:

1. **Given** a window 1024px or wider, **When** DigiDollar Receive opens, **Then** the address and its buttons are above the QR code, the QR code is smaller (144px), and "Scan with your phone" is shown under it.
2. **Given** a narrower window, **When** DigiDollar Receive opens, **Then** the QR code is first and full size, and no caption is shown.
3. **Given** either width, **When** the person picks another of their DigiDollar addresses, makes a new one, copies, shares or downloads the card, **Then** these work as today.

---

### User Story 3 - Shared-wallet Receive leads with the address on wide screens (Priority: P2)

The shared-wallet Receive sheet follows the same order: on a wide window the address and **Copy** / **Share** come first, then a smaller QR code with "Scan with your phone".

**Independent Test**: Open a shared wallet's Receive at 1280px and 390px and compare the order as in Story 2.

**Acceptance Scenarios**:

1. **Given** a window 1024px or wider, **When** the shared-wallet Receive sheet opens, **Then** the address and its buttons come before the QR code, which is smaller and captioned.
2. **Given** a narrower window, **When** it opens, **Then** it looks as today.

---

### User Story 4 - Home's empty state says who its QR code is for (Priority: P3)

On a wide window, Home's empty-state card shows "Scan with your phone" under its small QR code. The card keeps its side-by-side layout on every width.

**Acceptance Scenarios**:

1. **Given** a window 1024px or wider and a wallet with no transactions, **When** Home opens, **Then** the empty-state card shows the caption under its QR code.
2. **Given** a narrower window, **When** Home opens, **Then** the card is as today, with no caption.

---

### Edge Cases

- **A payment read in the menu while the wallet is locked.** The menu is only shown in an unlocked wallet. If the wallet locks between the scan and Send, the unlock flow returns to Send's address (`/send?link=n`), which carries nothing; the waiting payment is still in memory and is filled in after unlocking, as with spec 030's payment links.
- **A payment waiting for one Send is taken by another.** A waiting payment names the Send it is for (personal, or one shared wallet). Any Send that opens takes what is waiting and discards it if it was for another Send, so an old payment never turns up later.
- **A new tab or a reload.** Nothing is waiting there; the form is empty.
- **An address valid only on the other DigiByte network.** It is refused or warned about by Send's own checks, exactly as it is today when it arrives in the address bar.
- **The Spanish wallet.** The only new visible text, the caption, already exists in English and Spanish (`receive.qr_for_phone`).

## Requirements *(mandatory)*

### Functional Requirements

**Payments read in the menu**

- **FR-001**: Every payment the menu's Paste / Scan reads (a bare address, a `digibyte:` request, or this wallet's payment link), whatever the source (typed, pasted, dropped, picture, screen or camera), MUST be handed to Send in memory. The address bar MUST NOT show the address, the amount or any note at any step, unlocking included.
- **FR-002**: With the personal wallet open, the payment MUST go to the personal Send. With a shared wallet open, it MUST go to that shared wallet's Send. This keeps spec 026's rule (FR-002a).
- **FR-003**: The handed-over payment MUST carry only the address and the amount. A label or message MUST NOT reach Send's memo.
- **FR-004**: The hand-over MUST be one-shot and in memory only: taken once by the Send it names, discarded when another Send takes it, never stored. A reload or a new tab starts empty.
- **FR-005**: The kinds of code the menu understands, and where each non-payment kind goes, MUST NOT change. Whether a code is a payment MUST be decided by the same rule that routes scans, so the hand-over and the route can never disagree.
- **FR-006**: Send (personal and shared) MUST apply its usual checks to a handed-over payment, as it does to typed values. Nothing is sent without the person's review and confirmation.
- **FR-007**: When the shared wallet's Send cannot open, **Pay from my wallet** MUST offer the same payment from the personal wallet, handed over in memory.
- **FR-008**: Send's existing `?address=&amount=` reading MUST keep working for links that already use it; only the menu stops producing such addresses.

**Layout on wide screens**

- **FR-009**: At 1024px and wider, DigiDollar Receive and the shared-wallet Receive sheet MUST show the address and their Copy and Share buttons before the QR code, with the QR code at 144px and captioned "Scan with your phone". Narrower windows MUST NOT change.
- **FR-010**: At 1024px and wider, Home's empty-state card MUST show the same caption under its QR code. Its layout MUST NOT otherwise change at any width.
- **FR-011**: These layouts MUST reuse spec 030's `.receive-main`, `.receive-main-qr` and `.receive-qr-caption` rules, so all receive screens switch at the same width.
- **FR-012**: No screen in this spec gains **Copy payment link**.

**Language**

- **FR-013**: Any new text MUST exist in English and Spanish, and the locale parity tests from spec 028 MUST pass.

### Key Entities

- **Waiting payment**: an address, an optional amount, and the Send it is for (personal, or a shared wallet's id). Held in memory until a Send takes it.

## Success Criteria *(mandatory)*

- **SC-001**: For every payment kind the menu reads (bare address, legacy address, `digibyte:` request with and without amount, payment link), with the personal wallet and with a shared wallet open, the resulting address bar contains no address and no amount. Checked by automated tests of the route and the hand-over, and by hand in the browser.
- **SC-002**: Every non-payment code kind routes exactly as before, checked by the existing route tests, which pass unchanged.
- **SC-003**: At 1280px, DigiDollar Receive and the shared-wallet sheet show the address above the QR code; at 390px they look as before. Checked in the browser.
- **SC-004**: The locale parity tests pass.

## Assumptions

- The wide-screen breakpoint is spec 030's (1024px).
- The shared-wallet Receive sheet is a dialog outside the 480px app column; at 1024px and wider it still stacks in one column, address first.
- Out of scope: Share account xpub (`/settings/xpub`), payment links without an amount, a "request an amount" option on the shared-wallet sheet, and DigiDollar payment links.
- Depends on spec 030 (`PaymentHandoff`, `/pay`, the wide-screen CSS), which is not yet merged; this branch is cut from `feature/desktop-paste-scan`.
