# Feature Specification: Requests That Know When They're Paid

**Feature Branch**: `feature/paid-aware-requests` (spec directory `036-paid-aware-requests`)

**Created**: 2026-11-28

**Status**: Draft

**Input**: User description: "Paid-aware payment requests in the wallet's Request tab (/payments, Payments.razor). Today a request is a saved QR/link that reuses the wallet's current receiving address and never learns whether it was paid; Status (Pending/Paid/Expired/Cancelled) and ExpiresAt exist on PaymentRequest but are unused, DeleteAsync is not wired to the UI, and the message isn't shown. Goal: make Request something people use every day. Prioritized user stories: (P1) Each request gets its own fresh receiving address (reusing the spec 027 watched-address machinery: MoveAddressOnShow / WatchedReceiveIndexes) and shows live status: Waiting, Seen (unconfirmed), Paid, Partly paid, Overpaid, Expired, Cancelled. (P2) A clear \"Paid!\" moment: when payment arrives while the request is on screen, a full-screen confirmation with haptic on phones; a notification when the app is in the background. (P3) Request in local currency: the user types an amount in their fiat currency (FiatCurrencies already exists), the DGB amount is locked at the current rate for a limited window, and the request shows a countdown to expiry. (P4) Fast keypad: opening the Request tab goes straight to a big amount keypad, one tap to show the QR; label/message stay optional behind a \"more\" control. Also in scope: delete/cancel a request, Open/Paid/Expired filter, show the message on the detail view. Out of scope for this spec (later specs): request from a contact, split bill, reusable/recurring requests, DigiDollar requests, shop/POS mode. Must stay self-custodial and privacy-preserving: the payment link keeps the request after the # (spec 030 FR-019), no server learns the request. Shared/multisig wallets keep their Pending tab and are not changed."

## Context

**What the Request tab does today.** A person fills in an optional amount in DGB, a label and a message, and gets a QR code, a `digibyte:` link and a web payment link (spec 030). The request is saved on the device and listed by date. That is all. The request uses whatever address the wallet is showing on Receive at that moment, so two requests made a few minutes apart usually share one address. The wallet never looks to see whether a request was paid, a request can never be removed, and the message the person typed is put in the link but never shown back to them.

**Why that keeps people away.** The moment that matters when you ask someone for money is "did it arrive?". Today the person has to leave the request, open Activity and work out which incoming payment belongs to which request. With shared addresses that is sometimes impossible. Most people also think of prices in their own currency, not in DGB.

**What this feature changes.** Every new request gets an address of its own, so the wallet can tell which request a payment belongs to and show the request as waiting, payment arriving, paid, partly paid or overpaid. When a payment arrives while the request is open, the wallet says so unmistakably. A request can be made in the person's own currency, at a rate held for a short time. Opening the Request tab goes straight to a large keypad, so asking for money is a few taps.

**Limits that come from the wallet itself.** The wallet will not hand out a new address while 20 unpaid ones are outstanding (spec 027). That rule protects restores from seed and stays. It does limit how many unpaid requests can be open at once (see FR-005).

## Clarifications

### Session 2026-11-28

- Q: What should happen when the wallet already has 20 unpaid addresses handed out in a row and the person makes a new request? → A: Refuse. The wallet does not make the request, says how many open requests are unpaid, and offers a shortcut to the Open list to cancel old ones. It never reuses an address or falls back to the shared Receive address.
- Q: At what point should the wallet tell the person a request is paid? → A: In two steps. When the payment is seen on the network it shows "Payment arriving" with the amount; at the first confirmation it shows "Paid". The full-screen confirmation, vibration and notifications fire only at "Paid".
- Q: When requests were paid while the wallet was closed, what should the person see the next time they open it? → A: A one-time in-app message ("2 requests paid since you were last here", tap to open the list), plus a count on the Request tab until the person views those requests. No full-screen confirmation for them.
- Q: Which currency should the Request keypad start in? → A: The one the person used for their last request; DGB the very first time and whenever no price is available.
- Q: When a currency request expires after only part of the payment arrived, what status should it show, and can it still be shared? → A: "Partly paid · expired" with the amount received; its QR code and links are no longer offered; renewing asks only for what is still owed, at today's price.
- Q: Should a currency request's link tell the payer about the price lock? → A: Yes. The link carries the expiry time and the currency amount. This wallet's Send and the web payment page show "Price held until…", warn once it has passed, and still let the payer go ahead. Other wallets ignore the extra fields.
- Q: Should people be able to choose to show the amount and label in "request paid" system notifications? → A: Yes, with a setting "Show amount and label in notifications", off by default. When on, the notification shows the amount and label, never the address.
- Q: When part of a request's payment is confirmed and more is still on its way, what status should the request show? → A: The status follows the confirmed money (for example "Partly paid"), with a note such as "+40 DGB arriving" while the rest is unconfirmed. "Payment arriving" is the status only when nothing has confirmed yet.
- Q: After a person removes a request from the list, should Activity still show that request's label on its payments? → A: Yes. Payments already received keep the label in Activity; removing a request only takes it out of the request list.
- Q: If the wallet auto-locks while a request is waiting to be paid, what should happen? → A: Locking works as today, but the wallet keeps checking open requests' addresses while locked. The lock screen shows "Request paid" (no amount or label unless the notification setting from FR-013 is on), and system notifications still fire while the wallet is locked or hidden.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See whether a request was paid (Priority: P1)

A person makes a request, sends the link or shows the QR code, and later opens the Request tab. Each request shows where it stands: Waiting, Payment arriving (the payment is on the network but not yet in a block), Paid, Partly paid, Overpaid, Expired or Cancelled. Opening a request shows how much has arrived against how much was asked for.

**Why this priority**: Without this, a request is just a QR code, and every other story depends on the wallet knowing which payment belongs to which request.

**Independent Test**: Create two requests for different amounts, pay one in full and the other in part from another wallet, and check that each shows its own correct status and amount received without opening Activity.

**Acceptance Scenarios**:

1. **Given** a personal wallet, **When** the person creates two requests one after the other, **Then** each request has a different receiving address.
2. **Given** an open request for 100 DGB, **When** a 100 DGB payment to its address is seen on the network but not yet in a block, **Then** the request shows "Payment arriving" with the amount.
3. **Given** a request showing "Payment arriving", **When** the payment reaches its first confirmation, **Then** the request shows "Paid".
4. **Given** an open request for 100 DGB, **When** 60 DGB arrives, **Then** the request shows "Partly paid", 60 of 100 DGB received, and stays open for the rest.
5. **Given** an open request for 100 DGB, **When** 120 DGB arrives, **Then** the request shows "Overpaid" and the extra 20 DGB.
6. **Given** a request with no amount, **When** any payment arrives, **Then** the request shows "Paid" and the amount received.
7. **Given** a request for 100 DGB with 60 DGB confirmed, **When** another 40 DGB is seen but not yet in a block, **Then** the request shows "Partly paid" with "+40 DGB arriving".
8. **Given** a request that is partly paid, **When** a second payment to the same address makes up the rest, **Then** the request shows "Paid" with the total of both payments.
9. **Given** a paid request, **When** the person opens Activity, **Then** the incoming transaction shows the request's label.

---

### User Story 2 - Know the moment it's paid (Priority: P2)

A person shows a request's QR code to someone in front of them. As soon as the payment arrives, the screen changes to a large, unmistakable "Paid" confirmation with the amount, and on phones that support it, the device vibrates briefly. If the person has switched to another screen of the wallet, or another tab, they still get told.

**Why this priority**: This is what makes the Request tab worth using in person (a market stall, paying back a friend), instead of reading out an address.

**Independent Test**: Keep a request open on screen, pay it from another wallet, and check that the confirmation appears without any action and within the time set in SC-002.

**Acceptance Scenarios**:

1. **Given** a request open on screen, **When** its payment is seen on the network but not yet in a block, **Then** the screen shows "Payment arriving" with the amount, without vibrating.
2. **Given** a request showing "Payment arriving", **When** the payment reaches its first confirmation, **Then** a full-screen confirmation shows the amount received and the request's label, and the device vibrates if it can.
3. **Given** the confirmation is showing, **When** the person taps Done, **Then** they return to the request, which now shows its paid status.
4. **Given** a request was paid while the person was on another screen of the wallet, **When** the payment arrives, **Then** an in-app message names the request and the amount, and tapping it opens the request.
5. **Given** the wallet is open in a background tab or a backgrounded app, and the person has allowed notifications, **When** a request is paid, **Then** a system notification says a request was paid. It shows no amount, label or address unless the person turned on "Show amount and label in notifications", in which case it shows the amount and label but still no address.
6. **Given** two requests were paid while the wallet was closed, **When** the person opens the wallet, **Then** one in-app message says two requests were paid and the Request tab shows a count of 2, which drops as each request is opened.
7. **Given** a request's QR code is on screen and the wallet auto-locks before the payer pays, **When** the payment reaches its first confirmation, **Then** the lock screen shows "Request paid" (no amount unless the FR-013 setting is on), and after unlocking the person sees the request as Paid.
8. **Given** a partly paid request open on screen, **When** a partial payment arrives, **Then** the screen updates to "Partly paid" without the full-screen Paid confirmation.

---

### User Story 3 - Ask in my own currency (Priority: P3)

A person types "12.50" with their own currency selected (for example USD or EUR, as set in Settings). The wallet works out the DGB amount at the current price and holds that price for a limited time. The request shows both amounts and a countdown. When the time runs out, the request expires and the person can renew it at the new price with one tap.

**Why this priority**: Most people price things in their own currency. Without a price lock, the payer and the requester can disagree about what was owed.

**Independent Test**: Make a request for 10.00 in the person's currency, check that the DGB amount matches the shown price, let the countdown run out and check that the request shows "Expired" and offers to renew it at a new price.

**Acceptance Scenarios**:

1. **Given** a price is available, **When** the person enters an amount in their own currency, **Then** the keypad shows the matching DGB amount as they type, and the request is created for that DGB amount with the price it used.
2. **Given** a request made in the person's own currency, **When** it is shown, **Then** it shows the currency amount, the DGB amount, the price used and the time left.
3. **Given** a currency request whose time has run out without payment, **Then** it shows "Expired" and offers "Renew at today's price", which creates a new request for the same currency amount with a new address.
4. **Given** a currency request for 20.00 whose time ran out after half its DGB amount was confirmed, **Then** it shows "Partly paid · expired" with the amount received, no longer offers its QR code or links, and "Renew at today's price" asks for only the DGB still missing, at the current price.
5. **Given** a currency request that has expired, **When** a payment arrives at its address anyway, **Then** the payment is recorded against that request and shown with a note that it was paid after the price expired. It is never lost or left out of the balance.
6. **Given** the price cannot be fetched, **When** the person opens the keypad, **Then** their own currency is unavailable, the keypad says why, and requests in DGB still work.

---

### User Story 4 - Ask for money in a few taps (Priority: P4)

Opening the Request tab goes straight to a large number keypad. The person types an amount and taps one button to show the QR code. Label and message are under an optional "Add a note" control. The list of past requests is one tap away.

**Why this priority**: Everyday use depends on speed, but this only helps once requests are worth making (stories 1 to 3).

**Independent Test**: From the wallet's home screen, time how long it takes to get a QR code for a 25 DGB request on screen.

**Acceptance Scenarios**:

1. **Given** a personal wallet, **When** the person opens the Request tab, **Then** a keypad with an amount display and a currency switch (DGB or their own currency) is shown first.
2. **Given** the keypad, **When** the person taps "Request" without typing an amount, **Then** a request for any amount is made.
3. **Given** the keypad, **When** the person opens "Add a note", **Then** they can enter a label and a message before making the request.
4. **Given** the keypad, **When** the person taps the requests list control, **Then** they see their past requests with their statuses.
5. **Given** a request with a message, **When** it is opened, **Then** the message is shown on the request.

---

### User Story 5 - Keep the list tidy (Priority: P5)

A person can cancel a request they no longer want paid, remove old requests from the list, and filter the list to Open, Paid or Expired.

**Why this priority**: Once people use the tab daily the list grows quickly. This keeps it usable but is not needed to get value from stories 1 to 4.

**Independent Test**: Create three requests, pay one, cancel one, and check that each filter shows the right ones and that a removed request disappears from the list.

**Acceptance Scenarios**:

1. **Given** an open request, **When** the person cancels it, **Then** it shows "Cancelled" and its QR code and links are no longer offered for sharing.
2. **Given** a cancelled request, **When** a payment arrives at its address anyway, **Then** the payment is counted in the balance and shown on the request with a note that it arrived after cancelling.
3. **Given** a request in the list, **When** the person removes it and confirms, **Then** it disappears from the list. Any money already sent to its address stays in the wallet and in Activity, still showing the request's label.
4. **Given** requests in several states, **When** the person picks the Open, Paid or Expired filter, **Then** only matching requests are listed. Open covers Waiting, Payment arriving and Partly paid. Paid covers Paid and Overpaid. Expired covers Expired, Partly paid · expired and Cancelled. All lists every request, including Not tracked ones.

---

### Edge Cases

- **Requests made before this update** share an address with each other or with Receive. The wallet cannot tell which payment belongs to which, so they are shown as "Not tracked", keep working as before (QR code, links) and are never given a status by guessing.
- **The person's Receive screen** must not show an address that belongs to a request. Receive keeps its own address. A payment to a request's address counts in the balance like any other incoming payment.
- **Too many unpaid requests open at once**: the wallet refuses a new request and points to the Open list (FR-005). Cancelling does not lift the limit, so the message says so instead of implying it does.
- **A payment that never confirms** or is replaced: the request goes back from "Payment arriving" to its earlier status and never shows Paid, vibrates or sends a notification for it.
- **Two payers pay the same request**: payments to its address add up; the request shows Overpaid if the total is more than asked for.
- **The wallet is offline or the block explorer cannot be reached**: statuses show the last known state, marked as not up to date, and creating requests still works (Principle V). A currency request cannot be made without a price.
- **The device clock is wrong**: expiry is measured from when the request was made on this device. A clock change does not mark a paid request as expired.
- **The person removes a request, then a payment arrives at its address**: the money is in the balance and Activity, still showing the label the request gave its address; the removed request does not come back.
- **The same wallet open in two tabs or on two devices restored from one seed**: requests are kept per device, as today. A request made on one device is not shown on the other, but its payment still appears in the balance on both. In two tabs on one device, a paid request is announced in only one tab.
- **Watch-only wallets**: requests work the same way. A watch-only wallet can make addresses and see payments.
- **Single-key (non-HD) wallets** have only one address, so every request shares it. They keep today's behaviour, shown as "Not tracked", with a note that this kind of wallet cannot tell requests apart.
- **A payer opens an expired currency request's link**: this wallet's Send and the web payment page warn that the price has expired and let the payer go ahead; the requester's wallet records the payment late (FR-020). A link with an expiry time that cannot be read is treated as having none.
- **The wallet locks while a request waits**: the lock does not stop status checks for open requests (FR-014b). The lock screen shows only that a request was paid; nothing else about the wallet becomes visible.
- **Shared (multisig) wallets** keep their Pending tab in place of Request and are not changed by this spec.

## Requirements *(mandatory)*

### Functional Requirements

**Addresses and status**

- **FR-001**: Each new request in an HD personal wallet MUST get a receiving address that no other request and no Receive screen has shown.
- **FR-002**: A request's address MUST be watched for incoming payments from the moment it is created, for as long as the wallet exists, whatever the request's status, so that money sent to it is never left out of the balance. A request being removed, cancelled or expired MUST NOT stop its address being watched.
- **FR-003**: Handing out a request's address MUST NOT change the address the Receive screen shows.
- **FR-004**: The wallet MUST keep each request's status up to date from payments to its address: Waiting (nothing received), Payment arriving (money seen on the network, none of it confirmed yet), Partly paid (confirmed total less than asked), Paid (confirmed total equal to the amount asked, or any confirmed amount for a request with no amount), Overpaid (confirmed total more than asked), Expired (time ran out with nothing confirmed), Partly paid · expired (time ran out with some but not all of the amount confirmed), Cancelled (cancelled by the person). Once any money has confirmed, the status follows the confirmed total, and any further unconfirmed money MUST be shown alongside it as a note (for example "Partly paid · +40 DGB arriving") until it confirms or drops out.
- **FR-005**: When the wallet already has 20 addresses handed out and unpaid in a row (the limit from spec 027) and the person makes a new request, the wallet MUST refuse to make it, MUST say how many addresses are waiting unpaid and how many of them are open requests, and MUST offer a shortcut to the Open list where old requests can be cancelled. It MUST NOT reuse another request's address or fall back to the Receive address. Cancelling a request does not free its address (FR-002); the limit lifts only as addresses are paid, as spec 027 already defines.
- **FR-006**: A request's status MUST be worked out only from payments to its own address. Requests made before this update, and requests in single-key wallets, MUST be shown as "Not tracked" and never given a status by guessing.
- **FR-007**: Opening a request MUST show its status, the amount received against the amount asked, each payment received with its time, and the message if there is one.
- **FR-008**: A payment received against a request MUST show the request's label in Activity.
- **FR-009**: A payment that drops out of the network or is replaced before it confirms MUST stop counting towards its request.

**The paid moment**

- **FR-010**: The wallet MUST tell the person in two steps. When a payment that would complete the request is seen on the network but not yet in a block, a request open on screen MUST show that it is arriving (the "Payment arriving" status, or the "+… arriving" note when some money has already confirmed, FR-004) with the amount, clearly worded as not yet final. When the request's confirmed total first reaches the amount asked (or any amount, for a request with no amount), the full-screen confirmation, vibration and notifications MUST fire, once per request. "Payment arriving" MUST NOT trigger vibration or notifications.
- **FR-011**: The full-screen confirmation MUST show the amount received and the request's label, MUST vibrate the device briefly where the device supports it, and MUST be dismissed by the person, never by a timer alone.
- **FR-012**: When a request is paid while the person is on another screen of the wallet, the wallet MUST show an in-app message naming the request and the amount, which opens the request when tapped.
- **FR-013**: When the wallet is open but not visible, and the person has allowed notifications, the wallet MUST show a system notification that a request was paid. It MUST NOT include the amount, label or address unless the person turned on a setting "Show amount and label in notifications" (off by default); with it on, the notification MUST show the amount and label and MUST still never show the address. Asking for permission MUST happen only after the person makes their first request, never on page load.
- **FR-014**: The wallet MUST NOT use a push service or any server to deliver these notifications. They are only shown while the wallet is open in a tab or app.
- **FR-014a**: When the wallet opens (or is unlocked, or the person switches to another wallet) and finds requests that became Paid or Overpaid while it was closed, it MUST show one in-app message saying how many (for example "2 requests paid since you were last here"), which opens the request list when tapped. It MUST NOT show the full-screen confirmation for them. The Request tab MUST show a count of paid requests the person has not yet opened, which goes down as each one is opened.
- **FR-014b**: Auto-lock (after inactivity, or when the wallet is hidden) MUST keep working exactly as today. While locked, the wallet MUST keep checking the addresses of open requests, using only those public addresses and no key or seed. When a request becomes Paid or Overpaid while locked, the lock screen MUST show "Request paid", with the amount and label only if the setting in FR-013 is on, and the FR-013 system notification MUST still fire if the wallet is hidden. Unlocking then follows FR-014a for any requests paid while locked. The full-screen confirmation (FR-011) MUST NOT appear over the lock screen.
- **FR-015**: Partial payments MUST update the request on screen without the full-screen Paid confirmation.

**Own currency**

- **FR-016**: The person MUST be able to enter a request amount in their own currency, as chosen in Settings, or in DGB, and switch between the two on the keypad.
- **FR-016a**: The keypad MUST start in the currency (DGB or the person's own) used for their last request. It MUST start in DGB the first time, and whenever no price is available, without changing the remembered choice.
- **FR-017**: A request made in the person's own currency MUST fix its DGB amount at the price shown when it was made, MUST record the currency, the currency amount and the price, and MUST expire 15 minutes after it was made unless paid in full.
- **FR-018**: A currency request MUST show the time left while it is open, and once expired MUST offer to renew it, which creates a new request for the same currency amount at the current price with a new address. For a request that is Partly paid · expired, renewing asks only for the part still owed, converted from the fixed DGB amount still missing at the current price. The expired request stays in the list. An expired request, partly paid or not, MUST NOT offer its QR code or links for sharing.
- **FR-019**: A request in DGB MUST NOT expire on its own.
- **FR-020**: A payment arriving after a request expired or was cancelled MUST be recorded against that request, counted in the balance and shown with a note saying it arrived late or after cancelling. The request's status MUST then reflect what arrived against the fixed DGB amount: Paid or Overpaid once the total is reached, otherwise Partly paid · expired (or Cancelled, with the amount shown, for a cancelled request).
- **FR-021**: When no price is available, the own-currency option MUST be unavailable with a reason shown, and requests in DGB MUST keep working.

**Keypad and list**

- **FR-022**: Opening the Request tab in a personal wallet MUST show the amount keypad first, with one button that makes the request and shows its QR code.
- **FR-023**: Label and message MUST be optional and behind an "Add a note" control on the keypad.
- **FR-024**: The list of requests MUST be reachable from the keypad in one tap, MUST show each request's label, amount, date and status, and MUST offer Open, Paid and Expired filters as defined in User Story 5, plus an All view that lists every request, including Not tracked ones, which appear only there.
- **FR-025**: The person MUST be able to cancel an open request. A cancelled request MUST NOT offer its QR code or links for sharing.
- **FR-026**: The person MUST be able to remove a request from the list after confirming. Removing a request MUST NOT affect the balance or Activity (payments already received keep the request's label there, FR-008), and MUST NOT stop its address being watched (FR-002).
- **FR-027**: Existing actions on an open request (copy pay link, share, copy URI, download QR) MUST keep working as today.

**Privacy and custody**

- **FR-027a**: The `digibyte:` link and web payment link of a currency request MUST also carry its expiry time and its currency amount, in fields that other wallets ignore, so the DGB address and amount still work everywhere. When this wallet's Send or the web payment page (spec 030) opens such a link, it MUST show the currency amount and "Price held until…" with the time; once that time has passed it MUST warn that the price has expired and the amount may be out of date, and MUST still let the payer go ahead. Requests in DGB MUST NOT carry these fields.
- **FR-028**: Requests, their labels, messages, amounts and statuses MUST be stored on the device only, as today. No server operated by this project may learn that a request exists.
- **FR-029**: The web payment link MUST keep the request after the `#`, so the web host never sees it (spec 030, FR-019).
- **FR-030**: Watching a request's address MUST use the same block explorer lookups the wallet already uses for its balance, and MUST NOT send labels, messages or amounts to any service.
- **FR-031**: The price for currency requests MUST come from the price source the wallet already uses, and MUST NOT include anything about the request.
- **FR-032**: Shared (multisig) wallets MUST keep their Pending tab and MUST NOT be changed by this feature.

### Key Entities

- **Payment request**: something the person asked to be paid. Has its own address (or "Not tracked" for older ones), an optional amount in DGB, an optional amount in the person's own currency with the price used, an optional label and message, the time it was made, an expiry time for currency requests, a status, whether it was cancelled, and whether the person has opened it since it was paid (for the Request tab count, FR-014a).
- **Received payment**: a payment that arrived at a request's address. Has the amount, whether it is confirmed, when it was seen, and whether it arrived after the request expired or was cancelled. A request can have several.
- **Watched address**: an address the wallet keeps checking for payments. A request's address is one, joining the set spec 027 already keeps, and stays in it for good.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In testing, every request made after this update shows the correct status for full, partial, over and no payment, with no payment counted against the wrong request (100% in the test set).
- **SC-002**: When a request is open on screen, "Payment arriving" appears within 5 seconds of the wallet's data source reporting the unconfirmed payment, and "Paid" within 5 seconds of it reporting the first confirmation, measured as the median over 10 test payments.
- **SC-003**: From the wallet's home screen, a person can show a QR code for a request with an amount in under 10 seconds and in no more than 4 taps.
- **SC-004**: A person can tell whether a given request was paid without opening Activity, in every case covered by FR-004.
- **SC-005**: No money sent to any request's address is ever missing from the wallet's balance, whatever happened to the request (paid, expired, cancelled, removed).
- **SC-006**: No request detail (label, message, amount, status) appears in any request this wallet sends to a server operated by this project, checked by inspecting network traffic during the test runs.
- **SC-007**: Making a request in DGB and seeing its status keep working, marked as not up to date, while the price source or block explorer is unreachable.

## Assumptions

- "Paid" compares the confirmed total with the DGB amount of the request. Small differences are not rounded away: one satoshi short is Partly paid.
- New on-screen wording ships in every language the wallet already offers (including Spanish, spec 028).
- The 15-minute price lock for currency requests is a fixed default, not a setting, in this spec.
- The person's own currency is the one already chosen in Settings. Changing it later does not change existing requests.
- Request status is updated whenever the wallet refreshes its balance. While a request is open on screen, the wallet checks its address more often, at the pace the existing refresh tiers (spec 024) allow.
- Requests stay stored on the device only. Today one list is shared by every wallet on the device; this feature keeps a list per wallet, and moves each old request to the wallet its address belongs to. Moving requests between devices is out of scope.
- Requests' addresses are public. Requests are already stored on the device outside the password-protected vault (as they are today), so the wallet can read them while locked; checking them needs no key or password. Status checks while locked are done by a separate watcher that uses only those addresses (plan research R3).
- Notifications work only while the wallet is open in a tab or app. Telling the person when the wallet is fully closed would need a server and is out of scope.
- Out of scope for this spec: requesting from a contact, splitting a bill, reusable or recurring requests, DigiDollar requests, and a shop or till mode. Each is a candidate for a later spec.
- Besides the Request tab, this feature changes how this wallet's Send and the web payment page (spec 030) read a request link (FR-027a); nothing else on those screens changes.
- Depends on spec 027's watched addresses, spec 030's web payment link, spec 024's refresh tiers and the existing price and currency settings.
