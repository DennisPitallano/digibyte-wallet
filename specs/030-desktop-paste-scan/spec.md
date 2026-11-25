# Feature Specification: Paste, Drop and Link Instead of Scan on Desktop

**Feature Branch**: `feature/desktop-paste-scan` (planned; spec directory `030-desktop-paste-scan`)

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "On the desktop web version, since most of the users are not accessing it from their mobile phones, the QR scanning and code will mostly not be useful. What alternative can we offer that will work as QR scanning/code." Scope agreed afterwards: a paste-first Scan on desktop, reading a QR from a pasted or dropped image, reading a QR from another tab or window on screen, and https payment links on Receive and Request.

## Context

**What the person sees today.** The menu's middle button is **Scan**. Pressing it opens a camera view at once. On a desktop or laptop this either asks for camera permission, shows "camera failed" when there is no camera, or shows the person's own face. The only other option in that view is **Upload image**, which opens a file picker. Ten other screens open the same view from their own scan buttons: Send, Contacts, Digi-ID, Import multisig, Pending (multisig), a pending payment's detail, multisig Send, the signing room, Recover (private key) and DigiDollar Send.

On a desktop, the code the person needs is almost never on another device in front of the camera. It is on the same screen (a shop's checkout page, a chat, an email) or already copied as text. Turning that into a "scan" today takes several steps: save a screenshot as a file, press Upload image, find the file.

**Sharing has the same problem the other way round.** Receive and Request lead with a large QR code. When an amount is set, **Copy link** copies a `digibyte:` payment request. On a desktop, the person is usually sending that to someone far away by chat or email, and a `digibyte:` link does nothing when clicked in most desktop browsers and mail apps.

**What already exists to build on.** Every scanned code is only text. One shared rule already decides what any text means and where it goes: a payment (address or `digibyte:` request), a Digi-ID login, a multisig room invite (a link to this wallet), a multisig proposal, or a multisig redeem script. Anything else goes nowhere and the person is told what the scanner understands. The wallet can already read a QR code from an image without sending it anywhere, and it already opens Send with an address and amount filled in.

## Clarifications

### Session 2026-11-25

- Q: Should a payment link carry its address, amount and note after a `#` (never sent to the web host) or as a normal query that the host's logs record? → A: After `#`. The host only sees a request for the payment page. The payment details stay in the browser.
- Q: What does a person without this wallet see when they open a payment link? → A: A read-only payment page with the address, amount, note, QR code and copy buttons, plus "set up this wallet". They can pay from any wallet. Nothing on the page moves funds (FR-022).
- Q: How does the wallet decide to open the paste panel first instead of the camera? → A: When the device's main pointer is a mouse or trackpad, or it has no camera. Window width is not used (FR-001, FR-002).
- Q: Does Scan from screen ship in this release? → A: Yes, as the last priority (P3). It is built after paste, picture paste and drop, and payment links. If it slips, this release can ship without it and nothing else waits on it.
- Q: If someone switches to the camera, should the wallet open the camera first next time? → A: Yes. The last choice (paste or camera) is remembered on this device only and opens first next time (FR-004). There is no setting for it in Settings.
- Q: After a payment link opens Send, should the address and amount travel in Send's address bar (where a reload would send them to the host) or be handed over in memory? → A: In memory. Send's address stays `/send`, also through unlocking. A reload shows an empty Send.
- Q: If a browser refuses the `web+digibyte` handler pointing at `/pay#…`, what then? → A: Ship without the handler for that browser. There is no fallback that would put payment details in a request the host sees. Web payment links still work.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Paste instead of scan on a desktop (Priority: P1)

A person using the wallet on a laptop copies a DigiByte address from a chat and presses the menu's **Paste / Scan** button. Instead of a camera, a panel opens with a large field and a **Paste from clipboard** button. They press it (or press Ctrl+V / ⌘V). The wallet recognises the address and opens Send with it filled in, exactly as if they had scanned a QR code of it. Pasting a Digi-ID login link, a room invite, a multisig proposal or a redeem script goes where scanning it would go.

**Why this priority**: This is the most common desktop case and needs no camera, no screenshot and no file. Without it the menu's main button is useless on desktop.

**Independent Test**: On a desktop browser with no camera, press the menu button, paste each kind of code the scanner understands, and confirm each lands on the same screen, with the same values filled in, as scanning a QR code of it on a phone. Paste unrelated text and confirm nothing opens and the person is told what can be pasted.

**Acceptance Scenarios**:

1. **Given** a desktop browser (mouse or trackpad, or no camera), **When** the person presses the menu's middle button, **Then** the paste panel opens, the camera does not start, and no camera permission is requested.
2. **Given** the paste panel is open and the clipboard holds a valid DigiByte address, **When** the person presses **Paste from clipboard**, **Then** Send opens with that address filled in.
3. **Given** the clipboard holds a `digibyte:` payment request with an amount, **When** it is pasted, **Then** Send opens with the address and amount filled in.
4. **Given** the clipboard holds a Digi-ID link, a room invite to this wallet, a multisig proposal, or a redeem script, **When** it is pasted, **Then** the wallet goes to the same screen that scanning it would open.
5. **Given** the person types or pastes text into the field with the keyboard, **When** the text is complete, **Then** it is handled the same way, without needing a separate button press.
6. **Given** the pasted text is not something the wallet understands, **When** it is pasted, **Then** nothing opens, the field shows a short message saying what can be pasted, and the text can be corrected.
7. **Given** the paste panel is open, **When** the person chooses **Use camera**, **Then** the camera view opens as it does today.
8. **Given** a phone or tablet with a camera and a touch screen, **When** the person presses the menu's middle button, **Then** the camera view opens as it does today, with paste available as a secondary option.

---

### User Story 2 - The same paste panel on every screen that scans (Priority: P1)

The scan buttons on Send, Contacts, Digi-ID, Import multisig, Pending, a pending payment, multisig Send, the signing room, Recover and DigiDollar Send open the same panel on desktop. Whatever is pasted is handed to that screen just as a scan would be. For example, pasting an address on Contacts fills in the contact's address, and pasting a private key on Recover fills in the key field.

**Why this priority**: A person who learns paste in the menu expects the scan icon next to any field to work the same way. A camera view on one screen and a paste panel on another would be confusing.

**Independent Test**: On a desktop browser, open each of the ten screens listed, press its scan button, paste a valid value for that screen, and confirm the screen behaves as it does after a successful scan.

**Acceptance Scenarios**:

1. **Given** a desktop browser on any screen that has a scan button, **When** the person presses it, **Then** the paste panel opens rather than the camera.
2. **Given** the panel was opened from a screen's own scan button, **When** a value is pasted, **Then** that screen receives it exactly as it would receive a scanned value, and handles it with its own existing checks.
3. **Given** the panel was opened from Recover, **When** a private key is pasted, **Then** the key is passed to Recover, the panel's field is emptied immediately, and the key is not remembered anywhere once the panel closes.

---

### User Story 3 - Read a QR code from a screenshot or image (Priority: P2)

A shop's checkout page shows only a QR code. The person snips it with the screen-capture shortcut (for example Win+Shift+S on Windows, or ⌘⇧⌃4 on a Mac), opens the paste panel and presses Ctrl+V. The wallet reads the QR code from the picture and continues as if it had been scanned. Dragging a saved QR image onto the panel works the same way.

**Why this priority**: QR-only payment pages are common, and a QR code cannot be copied as text. This turns a four-step file upload into a paste.

**Independent Test**: Snip a QR code of an address to the clipboard, paste it into the panel, and confirm Send opens with the address. Drag a QR image file onto the panel and confirm the same.

**Acceptance Scenarios**:

1. **Given** the clipboard holds a picture with a readable QR code, **When** the person pastes into the panel, **Then** the wallet reads the code and handles it as a scan.
2. **Given** the person drags an image file onto the panel, **When** they drop it, **Then** the wallet reads the code and handles it as a scan.
3. **Given** a pasted or dropped picture has no readable QR code, **When** it is read, **Then** the person is told no code was found and can try again.
4. **Given** something is being dragged over the panel, **When** it is over the drop area, **Then** the area shows clearly that it will accept the drop.
5. **Given** a dropped file is not an image, or is larger than the size the wallet already accepts for uploads, **When** it is dropped, **Then** it is refused with a short reason, and nothing is read.

---

### User Story 4 - Read a QR code from another tab or window (Priority: P3)

The QR code is in another browser tab or app window. From the paste panel the person chooses **Scan from screen**. The browser asks which tab, window or screen to share. They pick the one showing the code. The wallet finds the QR code, stops sharing straight away, and continues as if it had been scanned.

**Why this priority**: This saves even the snip step. It depends on the browser supporting screen sharing and on the person granting it each time, so it is an extra, not the main path. It is built last, and the release can ship without it if it is not ready. No other story depends on it.

**Independent Test**: Open a page showing a payment QR code in one tab, start **Scan from screen** in the wallet, pick that tab, and confirm Send opens with the payment filled in and the browser's "sharing" indicator disappears.

**Acceptance Scenarios**:

1. **Given** a desktop browser that supports screen sharing, **When** the paste panel opens, **Then** a **Scan from screen** option is offered. Where screen sharing is not supported (for example most phones), the option is not shown.
2. **Given** the person picks a tab or window showing a readable QR code, **When** the wallet reads it, **Then** sharing stops at once and the code is handled as a scan.
3. **Given** sharing is active but no code is found, **When** the person closes the panel or presses Stop, **Then** sharing stops at once.
4. **Given** the person cancels the browser's sharing prompt or refuses it, **When** it closes, **Then** the panel stays open with no error beyond a short note, and the other options still work.
5. **Given** sharing is active, **When** about 60 seconds pass without finding a code, **Then** sharing stops and the person is told no code was found.

---

### User Story 5 - Share a payment link that works when clicked (Priority: P2)

On Receive (with an amount set) and on Request, the person presses **Copy payment link**. They get an ordinary web link to this wallet. They paste it into an email or chat. When the payer clicks it:
- if the payer uses this wallet, it opens Send with the address and amount filled in, for them to check and confirm;
- if the payer does not use this wallet, the page shows the address, the amount and a QR code, each with a copy button, so they can pay from any wallet.

**Why this priority**: Most desktop sharing goes by chat or email, where a QR code is an extra step and a `digibyte:` link usually does nothing.

**Independent Test**: Copy a payment link on Receive with an amount, open it in a browser where the wallet is set up and confirm Send opens with both values filled in. Open it in a private window with no wallet and confirm the payment details are shown and can be copied.

**Acceptance Scenarios**:

1. **Given** Receive with an amount set, or an existing payment request on Request, **When** the person presses **Copy payment link**, **Then** a web link to this wallet that carries the address, the amount and any note is copied, and the person sees that it was copied.
2. **Given** a payer with this wallet set up and unlocked, **When** they open the link, **Then** Send opens with the address and amount filled in, and nothing is sent until they review and confirm as usual.
3. **Given** the payer's wallet is locked, **When** they open the link, **Then** they are asked to unlock first and then land on Send with the values filled in.
4. **Given** a payer with no wallet on this device, **When** they open the link, **Then** they see the address, amount and note, a QR code of the payment, copy buttons for the address and the amount, and an option to set up this wallet. No money can move from this page.
5. **Given** a link with an address that fails the wallet's address checks (wrong network, bad checksum), a missing address, or an amount that is not a positive number, **When** it is opened, **Then** nothing is filled in and the person is told the link is not a valid payment request.
6. **Given** the existing `digibyte:` payment request, **When** a person wants it (for other wallets that read it), **Then** it is still available to copy, share and show as a QR code.
7. **Given** the wallet is installed as an app in a browser that accepts the handler, **When** the person clicks a `web+digibyte:` link, **Then** the wallet opens it the same way as the web payment link.

---

### User Story 6 - Receive leads with the address on wide screens (Priority: P3)

On a desktop-width window, Receive shows the address as text first, with **Copy address** and **Copy payment link** (when an amount is set) as the main buttons. The QR code is smaller and placed below or beside them, with a short label saying it is for scanning with a phone.

**Why this priority**: The layout should match what desktop people actually do. It is a layout change with no new behaviour, so it comes after the features that do something new.

**Independent Test**: Open Receive in a wide window and in a phone-width window, and confirm the wide layout leads with the address and buttons while the phone layout is unchanged.

**Acceptance Scenarios**:

1. **Given** a desktop-width window, **When** Receive opens, **Then** the address text and its copy buttons are the first things in the main card, and the QR code is still shown, smaller.
2. **Given** a phone-width window, **When** Receive opens, **Then** the layout is as it is today.
3. **Given** a desktop-width window, **When** Request shows a payment request, **Then** the same order applies: request details and **Copy payment link** first, QR code smaller.

---

### Edge Cases

- **A desktop with a webcam.** It still opens the paste panel first, because a mouse or trackpad means the code is most likely on screen. **Use camera** is one click away. If the person used the camera last time on this device, the camera opens first next time (see FR-004).
- **A touch-screen laptop or a tablet with a keyboard.** The device is treated as desktop if its main pointer is a mouse or trackpad. Otherwise it is treated as a phone. Both modes offer both options, so a wrong guess costs one click.
- **The browser will not let the page read the clipboard**, or the person refuses. The button's job falls back to the keyboard: the field is focused and the panel says "Press Ctrl+V (⌘V on Mac) to paste". Nothing breaks.
- **The clipboard is empty or holds something other than text or a picture.** The person is told there was nothing to paste.
- **Pasted text has spaces, line breaks or surrounding quotes** (common when copying from chat or email). Leading and trailing white space and line breaks are ignored before the text is read.
- **A secret is pasted where it is not expected**, for example a recovery phrase or private key pasted into the menu's Paste / Scan. It is not understood, so nothing opens. The field is cleared at once rather than keeping the secret on screen, and the person is told this kind of text cannot be used here.
- **A shared (multisig) wallet is open when a payment is pasted from the menu.** It goes to that shared wallet's Send, as a scanned payment does today.
- **A payment link pointing to a look-alike website.** Only links to this wallet's own address are treated as payment links or room invites. Any other web link pasted into the panel is not followed.
- **A payment link with an amount above the wallet's maximum, or more decimal places than DGB allows.** It is treated as invalid (FR-020) and nothing is filled in.
- **A payment link opened while another payment is half filled in on Send.** The link's values replace the address and amount, as a scan does today. The person still has to review and confirm.
- **A very large pasted text** (for example several kilobytes of hex for a multisig proposal). It is accepted up to 8,192 characters, which is more than the largest QR code can hold. Anything larger is refused with a short reason.
- **A payment link with a note.** The note is shown on the read-only page only. It is never put into Send's memo, because that memo is published on the blockchain.
- **Several QR codes in one picture, or on the shared screen.** The first one read is used. If it is not something the wallet understands, the person is told so and can crop and try again.
- **The panel is opened in Spanish.** All new labels, hints and messages appear in Spanish, including the keyboard hint and the "for scanning with a phone" label.

## Requirements *(mandatory)*

### Functional Requirements

**Choosing paste or camera**

- **FR-001**: On a device whose main pointer is a mouse or trackpad, or which has no camera, every scan action in the wallet MUST open the paste panel first, and MUST NOT start the camera or request camera permission until the person chooses the camera.
- **FR-002**: On a phone or tablet with a touch screen and a camera, every scan action MUST open the camera first, as today, with a visible option to paste instead.
- **FR-003**: Both the paste panel and the camera view MUST offer a one-step switch to the other.
- **FR-004**: The wallet MUST remember, on this device only, whether the person last chose paste or camera, and open that one first next time. This choice MUST NOT be stored anywhere but the device.
- **FR-005**: On desktop, the menu's middle button MUST read **Paste / Scan**, and its accessible name MUST say that it pastes or scans a code to pay, sign in or open a shared wallet item. On phones it MUST keep the label **Scan**.

**Paste panel: text**

- **FR-006**: The panel MUST contain a text field that has keyboard focus when the panel opens, and a **Paste from clipboard** button.
- **FR-007**: **Paste from clipboard** MUST read text (or a picture, see FR-012) from the clipboard and handle it at once. If the clipboard cannot be read, the field MUST stay focused and the panel MUST show a hint to press Ctrl+V or ⌘V.
- **FR-008**: Text pasted or typed into the field MUST be handled as soon as it is complete (on paste, or when the person presses Enter), with no separate confirm button needed.
- **FR-009**: Before reading, the wallet MUST ignore surrounding white space, line breaks and one pair of matching surrounding quotes.
- **FR-010**: When opened from the menu, pasted text MUST be routed by the same rule that routes a scanned QR code: the same kinds of code accepted, the same screens opened, the same values filled in, and the same refusal for anything else. The shared-wallet rule (a payment goes to the open shared wallet's Send) MUST apply as it does to scans.
- **FR-011**: When opened from a screen's own scan button, pasted text MUST be handed to that screen exactly as a scanned value would be, and that screen's existing checks MUST apply unchanged.
- **FR-011a**: When the panel is opened from a screen's own scan button and the value is a valid payment link to this wallet (FR-020), the panel MUST hand that screen the link's `digibyte:` request text, without its label or message, instead of the web link. A payment link pasted on any such screen then behaves exactly as scanning that `digibyte:` request would on that screen. Send, Contacts and multisig Send accept it. DigiDollar Send refuses it, as it refuses any DigiByte address. Any other web link is handed over unchanged.

**Paste panel: pictures**

- **FR-012**: Pasting a picture (from the clipboard or with Ctrl+V / ⌘V into the panel) MUST read the QR code from it, without sending the picture anywhere, and handle the result as a scan.
- **FR-013**: Dropping an image file onto the panel MUST do the same. While a file is dragged over it, the panel MUST show clearly where it can be dropped.
- **FR-014**: Files that are not images, or are larger than the existing upload limit (10 MB), MUST be refused with a short reason. The existing **Upload image** button MUST remain.

**Scan from screen**

- **FR-015**: Where the browser supports sharing a tab, window or screen, the panel MUST offer **Scan from screen**. Where it does not, the option MUST NOT be shown.
- **FR-016**: Scan from screen MUST read the shared picture only inside the browser, MUST NOT record or keep any of it, and MUST NOT send any of it anywhere.
- **FR-017**: Sharing MUST stop as soon as a code is read, when the person presses Stop or closes the panel, when the person leaves the screen, or after about 60 seconds with no code found.
- **FR-018**: Refusing or cancelling the browser's sharing prompt MUST leave the panel open and usable, with a short note and no error screen.

**Payment links**

- **FR-019**: Receive (when an amount is set) and Request MUST offer **Copy payment link**, which copies a web link to this wallet carrying the address, the amount and any note. The address, amount and note MUST be in the part of the link after `#`, so that opening the link never sends them to the web host or puts them in its logs. When the device can share, a share action MUST offer the same link. The existing `digibyte:` request MUST remain available to copy and as the QR code's content.
- **FR-020**: Opening a payment link MUST apply the same checks as a scanned payment (a link that is valid only on the other DigiByte network says so): the address MUST pass the wallet's network, version and checksum checks; the amount, if present, MUST be a positive number within the wallet's limits and decimal places. If any check fails, nothing MUST be filled in and the person MUST be told the link is not a valid payment request.
- **FR-021**: When a valid payment link is opened on a device with a wallet, Send MUST open with the address and amount filled in (after unlocking, if locked). Nothing MUST be sent without the person's usual review and confirmation. The address and amount MUST be handed to Send in memory and MUST NOT appear in the address bar at any step, unlocking included. Reloading Send afterwards shows an empty form.
- **FR-022**: When a valid payment link is opened on a device with no wallet, the page MUST show the address, the amount, any note, a QR code of the payment request, and copy buttons for the address and the amount, plus an option to set up this wallet. That page MUST NOT offer any way to move funds.
- **FR-023**: The installed app SHOULD register to open `web+digibyte:` links, handled by the same rules as the web payment link (FR-020 to FR-022). The handler MUST deliver the link only after `#`. Where a browser does not accept that, the handler MUST be left out rather than delivered any other way. The existing `web+digiid:` handling MUST be unchanged.
- **FR-024**: The menu's paste panel MUST recognise this wallet's own payment links as payments and, for the personal wallet, hand them to Send in memory as `/pay` does, so their details never appear in the address bar. Inside an open shared wallet the link starts a payment from that shared wallet, as a scanned payment does. Links to any other website MUST NOT be followed.

**Layout on wide screens**

- **FR-025**: On a desktop-width window, Receive and Request MUST show the address (or request details) and their copy buttons before the QR code, with the QR code smaller and labelled as being for scanning with a phone. The phone-width layout MUST NOT change.

**Privacy, secrets and language**

- **FR-026**: Nothing pasted, dropped, read from a picture or read from the screen MUST be sent to any server, logged, stored, or kept in any list of recent items. The panel's field MUST NOT offer browser auto-fill or spelling suggestions.
- **FR-027**: The panel's field MUST be emptied when the panel closes and as soon as its content has been handed on. When the content looks like a recovery phrase or a private key and the panel was opened from the menu, it MUST be cleared at once and the person told it cannot be used here.
- **FR-028**: Every new label, hint, message and accessible name MUST exist in English and Spanish, and the locale parity tests from spec 028 MUST pass.
- **FR-029**: The paste panel, drop area and Scan from screen MUST work with the keyboard alone and with a screen reader, including announcing the result of a paste or drop ("Address found, opening Send" or "No code found").

### Key Entities

- **Code text**: the text a person pastes or the wallet reads from a picture, camera or screen. It has no identity of its own and is never kept. Its meaning comes from the same routing rule used for scans: payment, Digi-ID login, room invite, multisig proposal, redeem script, or not understood.
- **Payment link**: a web link to this wallet's payment page, carrying a DigiByte address, an optional amount and an optional note after the `#`. It carries the same information as the existing `digibyte:` payment request, and the web host never receives that information.
- **Scan preference**: paste or camera, remembered on this device only, used to choose which one opens first.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a desktop with no camera, a person who has copied an address can go from pressing the menu button to Send with the address filled in using no more than 2 actions (open panel, paste) and in under 5 seconds.
- **SC-002**: On desktop, no scan action in the wallet triggers a camera permission request or a "camera failed" message unless the person chose the camera, now or the last time on this device.
- **SC-003**: For every kind of code the scanner understands, pasting the code's text opens the same screen with the same values as scanning its QR code, checked by automated tests covering 100% of the code kinds.
- **SC-004**: A QR code shown on another page can be turned into a filled-in Send by snip and paste in under 10 seconds, without saving a file.
- **SC-005**: A payment link copied from Receive opens, in a browser with the wallet, a Send screen with the correct address and amount in 100% of tested cases, and in a browser without the wallet shows the payment details with working copy buttons.
- **SC-006**: Invalid or tampered payment links (wrong network, bad checksum, negative or excessive amount, look-alike site) fill in nothing in 100% of tested cases.
- **SC-007**: Network inspection during paste, drop and Scan from screen shows no request carrying pasted text, pictures or screen content.
- **SC-008**: All new text is present in both English and Spanish, with no missing entries reported by the locale parity check.
- **SC-009**: In a usability check with at least 5 people on desktop, at least 4 pay a copied address and a QR code from another tab on their first try without help. The wallet collects no usage data, so this is checked by hand, not measured in production.

## Assumptions

- "Desktop" is decided by the device's main pointer (mouse or trackpad) and whether a camera is present, not by window width. Window width only decides the Receive and Request layout (FR-025), using the wallet's existing wide-screen breakpoint.
- Clipboard reading needs the person's action and, in some browsers, a permission prompt. Where it is unavailable, the keyboard paste path (Ctrl+V / ⌘V) is enough, so no browser is left without a way in.
- Scan from screen relies on the browser's own sharing prompt each time. The wallet never starts sharing without that prompt and cannot keep sharing in the background.
- The payment link uses the wallet's own public address (for example `https://dgbwallet.app/…`). A link copied on one deployment (for example a test site) points to that deployment.
- The page a payer without the wallet sees is part of the existing static wallet site and needs no new server. It only shows information already in the link.
- The scanner's existing image reader, size limit (10 MB) and routing rule are reused. No new kind of code is added, and the meaning of every existing code is unchanged.
- The private key path on Recover keeps its current behaviour after a value is handed over. This spec only changes how the value gets there and makes sure the panel does not keep it.
- Out of scope: pairing a phone to act as a remote scanner for the desktop, saving and opening multisig proposals as files, and changes to how QR codes are generated.
- Depends on spec 028 (English and Spanish text, locale parity tests) and on the existing unlock flow, which already sends a locked person to unlock and back (recent change: "Receive while locked goes to unlock").
