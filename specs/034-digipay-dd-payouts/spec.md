# Feature Specification: DigiDollar payouts to merchants

**Feature Branch**: `034-digipay-dd-payouts`

**Created**: 2026-11-27

**Status**: Draft

**Input**: User description: "DigiPay: pay out DigiDollar (DD) that DigiPay holds to the merchants it belongs to. Today a DD payment's address comes from DigiPay's own DigiDollar node, so DD paid to a store sits in addresses only DigiPay controls and can't be withdrawn; DGB payments go to the merchant's own xpub/address. Cover: merchant sets a DD payout address validated for the right network; when funds go out (automatic after confirmation, threshold, schedule) and who pays the DGB network fee (DigiPay covers DD fees from its own DGB wallet); Dashboard DD balance, payouts, failed payouts, and payout webhooks; security around changing the payout address (re-auth, delay, audit events, alerts); DD that arrived before payouts existed, including the store's DD donation address and extra DD on buyer-choice payments that needs refunding; behaviour when the DD node is down (constitution Principle V). Spec 033 (DD everywhere) depends on this."

## Background

DGB payments to a DigiPay store go straight to the merchant: DigiPay derives each payment address from the store's own public key (xpub) or uses the store's own address, so DigiPay never holds DGB.

DD works differently. There is no way yet to derive a DD address from a merchant's key, so each DD payment address is created by DigiPay's own DigiDollar node. The DD a buyer pays therefore lands in an address only DigiPay can spend from. Today DigiPay can only create those addresses, read their balances and find the payment that arrived. It can't send DD anywhere, so merchants have no way to get their DD out.

This feature lets each store name a DD address of its own and has DigiPay forward the store's DD there. It is a prerequisite for spec 033 (DD on the POS, payment links, buyer choice at checkout and donations), which must not ship until merchants can get their DD out (033 FR-032).

## Clarifications

### Session 2026-11-27

- Q: When a merchant changes their DD payout address, how long before it takes effect, and what happens to payouts meanwhile? → A: 48 hours, with payouts held (sent to neither the old nor the new address) until the change takes effect or is cancelled.
- Q: Does a store need a DD payout address before it can turn DigiDollar on? → A: Yes for stores turning DigiDollar on from now on. Stores that already have it on keep working without one and see a persistent prompt until they add one.
- Q: Who refunds extra DD a buyer sent (for example, paying in both currencies on a buyer-choice payment)? → A: The merchant. The extra DD is paid out with everything else, labelled "extra payment, refund the buyer", and the merchant refunds from their own wallet. DigiPay doesn't send refunds or collect buyer refund addresses.
- Q: When should payouts go out for a store that hasn't chosen? → A: As soon as each payment is confirmed. Merchants can switch to once a day or a balance threshold.
- Q: If DD was already paid out and the payment it came from is later undone by a chain reorganisation, who bears the loss? → A: DigiPay. It absorbs the loss, the operator is alerted, the merchant sees a note on the affected payment, and later payouts are never reduced.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Set a DD payout address and receive DD automatically (Priority: P1)

A merchant with DigiDollar on opens the store's DigiDollar settings in the Dashboard and pastes the DD address from their own wallet. The Dashboard checks that it is a DD address for the store's network, shows it back for confirmation, and saves it. From then on, each DD payment the store receives is sent on to that address once it is confirmed, with no action from the merchant.

**Why this priority**: Without this, DD paid to a store is stuck with DigiPay. Every other story builds on having a payout address and a payout running.

**Independent Test**: On a testnet store with DigiDollar on and no held DD, set a payout address from a test wallet. Create and pay a 5.00 DD payment through the API. Within the payout window after confirmation, the test wallet receives 5.00 DD and the Dashboard lists the payout with its transaction link.

**Acceptance Scenarios**:

1. **Given** a store with DigiDollar on and no payout address, **When** the merchant opens the DigiDollar settings, **Then** they see that DigiPay is holding the store's DD and a prompt to add a payout address.
2. **Given** the merchant pastes a valid DD address for the store's network, **When** they save, **Then** the Dashboard shows the address back (with its start and end highlighted) and asks them to confirm before it is saved.
3. **Given** the merchant pastes a DGB address, a DD address for the other network, a mistyped address, or one of DigiPay's own DD payment addresses, **When** they try to save, **Then** it is refused with a message saying which of these it is, and nothing is saved.
4. **Given** a payout address is in effect and payouts are set to "as soon as confirmed", **When** a DD payment to the store is confirmed, **Then** the full amount is sent to the payout address and appears in the store's payout list as sent, then confirmed.
5. **Given** a payout was sent, **When** the merchant opens it, **Then** they see the amount, the destination address, the payments it covers, the time sent, the network transaction link and its status.
6. **Given** a payout is sent, **When** it is created and confirmed, **Then** the merchant's DD amount is never reduced by a network fee: the store receives exactly the DD it was paid.

---

### User Story 2 - See DD balance, payouts and failures in the Dashboard (Priority: P1)

A merchant opens the Dashboard and sees, per store, how much DD DigiPay is holding for them right now, what is waiting to be paid out and why, and a list of payouts with their status. A payout that failed is clearly marked with a plain reason and what will happen next.

**Why this priority**: Merchants have to be able to trust that DigiPay is passing on all of their DD. Visible balances and payout history are how they check, and how support answers questions.

**Independent Test**: With a payout address set, pay two DD payments, stop the DigiDollar service before the payout runs, and check that the Dashboard shows the held balance, both payments as waiting, and the reason. Start the service again and see one or two payouts go out and the balance return to zero.

**Acceptance Scenarios**:

1. **Given** a store with DigiDollar on, **When** the merchant opens the store, **Then** they see the DD held for the store, split into confirmed and still confirming, and the time the balance was last checked.
2. **Given** DD is held but not yet paid out, **When** the merchant looks at the balance, **Then** they see why (waiting for confirmations, waiting for the schedule, below the threshold, no payout address, address change pending, payouts paused, DigiDollar service unavailable) and when the next payout is expected if known.
3. **Given** payouts have been made, **When** the merchant opens the payout list, **Then** they see each payout with amount, destination (shortened), date, status (sending, sent, confirmed, failed, checking) and transaction link, newest first, and can filter by status.
4. **Given** a payout failed, **When** the merchant views it, **Then** they see a plain reason, that the DD is still held for them, and whether DigiPay will retry automatically or needs something from them.
5. **Given** a payout failed, **When** the merchant chooses "Retry now", **Then** a new attempt is made for the held DD, following the same rules as an automatic payout.
6. **Given** a DD payment row in the payment list, **When** the merchant looks at it, **Then** it shows whether its DD has been paid out and links to the payout.

---

### User Story 3 - Change the payout address safely (Priority: P1)

A merchant moves to a new wallet and changes the store's DD payout address. Because changing where money goes is the most valuable thing an attacker with a stolen Dashboard session could do, DigiPay asks them to sign in again with Digi-ID, holds the change for a waiting period, tells them everywhere it can, and lets them cancel it.

**Why this priority**: A stolen session that could redirect payouts instantly would let an attacker take every future DD payment. This protection has to exist from the first release of payouts.

**Independent Test**: With a payout address in effect, request a change to a new address. Confirm a fresh Digi-ID sign-in is required, the Dashboard shows the pending change with a countdown, a webhook is sent, payouts are held, and the audit log records the request. Cancel it and confirm the old address stays in effect and payouts resume. Request it again, wait out the delay, and confirm the new address takes effect.

**Acceptance Scenarios**:

1. **Given** a payout address is in effect, **When** the merchant asks to change it, **Then** they must sign in again with Digi-ID before the change is accepted, even if their Dashboard session is still valid.
2. **Given** the merchant confirms a new address, **When** the change is accepted, **Then** it does not take effect until the waiting period (48 hours) has passed, and the Dashboard shows the new address, the time it will take effect and a "Cancel change" button.
3. **Given** a change is pending, **When** DD becomes ready to pay out, **Then** it is held (not sent to the old or the new address) until the change takes effect or is cancelled, and the balance explains why.
4. **Given** a change is pending, **When** the merchant (from any signed-in session) cancels it, **Then** the existing address stays in effect, held DD is paid out to it as normal, and the cancellation is recorded and announced like the request.
5. **Given** a change is requested, cancelled or takes effect, **When** that happens, **Then** an audit event is recorded with who did it, when, the old and new addresses, and the store; a webhook is sent; and a banner is shown on every Dashboard page for that merchant until they acknowledge it.
6. **Given** a merchant uses an API key, **When** they try to set or change a payout address through the API, **Then** the request is refused: payout addresses can only be set in the Dashboard.

---

### User Story 4 - DD received before payouts existed is paid out (Priority: P2)

A merchant who took DD payments through the API before this feature existed sets a payout address for the first time. The Dashboard shows how much DD DigiPay has been holding for the store, where it came from (payments, late or extra payments, donations), and pays it out once the address is in effect.

**Why this priority**: That DD already belongs to merchants. Getting it back to them is the reason this feature exists, but it only happens once per store, after Stories 1 and 3 are in place.

**Independent Test**: On a test store with DD held from earlier payments (including one overpaid payment and one that arrived after it expired), set a payout address. Confirm the Dashboard lists the held DD by source before the payout, that the waiting period applies, and that one payout then sends the full held amount.

**Acceptance Scenarios**:

1. **Given** a store holds DD from before payouts existed, **When** the merchant opens the store, **Then** they see the total held and a breakdown by source: confirmed payments, overpayments, payments that arrived after expiry, extra payments in the other currency on a buyer-choice payment, and donations.
2. **Given** a store holds DD and has never had a payout address, **When** the merchant sets one, **Then** the same re-authentication and waiting period as a change apply, because funds are already at stake.
3. **Given** a store with no held DD and no payout address, **When** the merchant sets one for the first time, **Then** it takes effect immediately after confirmation, without the waiting period, but still requires the Digi-ID sign-in and still raises the audit event, webhook and banner.
4. **Given** the waiting period ends, **When** the first payout runs, **Then** it sends all held, confirmed DD in one payout (or as few as possible) and lists which payments and donations it covers.
5. **Given** extra DD that should be refunded to a buyer is included in a payout, **When** the merchant views the payout or the payment, **Then** that amount is labelled "extra payment, refund the buyer" so the merchant knows to return it from their own wallet.

---

### User Story 5 - Choose when payouts go out (Priority: P2)

A merchant who prefers fewer, larger transfers sets the store to pay out once a day, or whenever the held DD reaches an amount they choose. They can also trigger a payout of everything confirmed at any time.

**Why this priority**: "As soon as confirmed" is the safest default, since DigiPay holds DD for as short a time as possible. Some merchants want tidier wallet histories or accounting, which matters less than getting the money out at all.

**Independent Test**: Set the store to a 50.00 DD threshold, receive 20.00 and 25.00 DD, and confirm nothing is paid out and the balance says why. Receive 10.00 more and confirm one payout of 55.00 DD. Then press "Pay out now" with 3.00 DD held and confirm it is sent.

**Acceptance Scenarios**:

1. **Given** the payout settings, **When** the merchant opens them, **Then** they can choose "as soon as confirmed" (default), "once a day" at a time they pick, or "when the balance reaches" an amount they enter.
2. **Given** "once a day", **When** the chosen time comes, **Then** all held, confirmed DD is paid out in one payout.
3. **Given** "when the balance reaches" an amount, **When** the confirmed held DD reaches it, **Then** it is all paid out. If it has not reached the amount within 7 days of the oldest held payment, it is paid out anyway.
4. **Given** any setting, **When** the merchant chooses "Pay out now", **Then** all confirmed held DD is paid out straight away, provided it meets the payout minimum.
5. **Given** a change of payout setting, **When** it is saved, **Then** it is recorded in the audit log, but it does not need re-authentication or a waiting period, because it can't send money anywhere new.

---

### User Story 6 - Get told about payouts in the merchant's own systems (Priority: P3)

A merchant whose own system tracks DigiPay webhooks gets a notification when a payout is sent, confirmed or failed, and when the payout address changes, so their books and alerts stay in step without checking the Dashboard.

**Why this priority**: Useful for integrated merchants and important as a second alert channel for address changes, but the Dashboard alone covers most merchants.

**Independent Test**: With a webhook URL set, run a payout and an address change, and confirm signed webhooks arrive for each step with the payout and address details, using the store's existing webhook signing.

**Acceptance Scenarios**:

1. **Given** a store with a webhook URL, **When** a payout is sent, confirmed or fails, **Then** a signed webhook is sent with the payout's id, amount, destination, status, transaction id (when there is one), the payments it covers and, for a failure, the reason.
2. **Given** a store with a webhook URL, **When** a payout address change is requested, cancelled or takes effect, **Then** a signed webhook is sent with the store, the old and new addresses and the time it takes or took effect.
3. **Given** webhook delivery fails, **When** DigiPay retries, **Then** payout webhooks follow the same retry and delivery log as payment webhooks.
4. **Given** a merchant's code reads payouts through the API with its API key, **When** it asks for a store's DD balance or payouts, **Then** it gets the same information the Dashboard shows, read-only.

---

### Edge Cases

- **DigiDollar service unavailable when a payout is due**: the payout waits, marked "waiting for DigiDollar service", not "failed". It is attempted again automatically when the service is back. The balance shows the last known amount and when it was last checked. DGB payments, the Dashboard and everything else keep working.
- **DigiDollar service unavailable when the merchant sets or changes the address**: the address is still checked (the check doesn't need the service) and the change can be saved. The waiting period runs as normal.
- **Unclear whether a payout went out** (for example the service stopped responding mid-send): the payout is marked "checking" and is never sent again until DigiPay has confirmed whether the first attempt reached the network. DD must never be paid out twice.
- **DigiPay's DGB fee wallet can't cover the network fee**: payouts wait, marked "delayed by DigiPay", the DigiPay operator is alerted, and no fee is ever taken from the merchant's DD.
- **Payout rejected by the network** (for example a rule change makes the address unspendable): the payout is marked failed with the reason, the DD stays held, and DigiPay retries on the next cycle. After 3 failures in a row, automatic payouts for that store pause and the merchant is asked to check the address.
- **Held DD below the payout minimum** (1.00 DD): it stays held and is shown as such until more DD arrives, except when the store is being deleted (see below).
- **Payment confirmed, then the chain reorganises and it is no longer confirmed**: DD is only paid out once a payment has the required confirmations. If a payment that was already paid out later disappears, DigiPay absorbs the loss: the operator is alerted, the merchant sees a note on the affected payment, and the store's later payouts and balance are not reduced. A payment that disappears before it was paid out is simply removed from the held balance.
- **DD arriving at a DigiPay address after the payment expired, or more than asked**: it belongs to the store and is paid out with everything else, labelled as late or overpaid, just as DGB late payments and overpayments reach the merchant today.
- **Extra DD on a buyer-choice payment (spec 033 FR-025)**: it belongs to the store's balance and is paid out, labelled "extra payment, refund the buyer". Refunding the buyer is the merchant's job, as it is for extra DGB today; DigiPay doesn't send refunds.
- **DD donations (spec 033 FR-011)**: DD sent to the store's DD donation address is added to the store's held DD and paid out with everything else, labelled as donations.
- **DigiDollar turned off for a store that still holds DD**: payouts keep running until the held DD reaches zero. Turning DigiDollar off only stops new DD payments.
- **Store deleted while holding DD**: deleting is blocked until held DD is paid out, or the merchant confirms a final payout of everything held to the address in effect. The DigiDollar network can't send less than 1.00 DD, so any remainder below that moves to the merchant's account-level DD balance (FR-039) and is paid out from there once it reaches 1.00 DD. With no payout address and 1.00 DD or more held, deletion is blocked and the merchant is told why.
- **Two stores with the same payout address**: allowed. Each store's payouts and balance stay separate.
- **Payout address set to one of DigiPay's own DD addresses** (another store's payment or donation address, or this store's): refused, so DD can't loop inside DigiPay.
- **Network mismatch**: a testnet store accepts only testnet DD addresses and a mainnet store only mainnet ones.
- **Operator pauses all payouts** (for example during an incident): payouts wait, every affected merchant sees "payouts paused by DigiPay" with the time it started, and nothing is marked failed.
- **Merchant's Digi-ID sign-in fails or is abandoned during an address change**: nothing changes, and the attempt is recorded in the audit log.

## Requirements *(mandatory)*

### Functional Requirements

**Payout address**

- **FR-001**: Each store MUST be able to have one DD payout address, set only from the merchant Dashboard.
- **FR-002**: A payout address MUST be checked before it is saved, and again before every payout: it MUST be a valid DD address (correct format and checksum) for the store's network. DGB addresses, other-network addresses and DigiPay's own DD payment or donation addresses MUST be refused, with a message that says which problem was found.
- **FR-003**: The Dashboard MUST show the address back to the merchant, with its start and end characters highlighted, and require them to confirm it before it is saved.
- **FR-004**: Setting or changing a payout address MUST need a fresh Digi-ID sign-in, made for that action, even if the Dashboard session is still valid.
- **FR-005**: A change to an existing payout address, or a first address on a store that already holds DD, MUST take effect only after a 48-hour waiting period. A first address on a store holding no DD MUST take effect straight away.
- **FR-006**: While a change is pending, payouts for the store MUST be held (sent neither to the old nor to the new address), and the Dashboard MUST show the pending address, when it takes effect, and a way to cancel it.
- **FR-007**: Any signed-in session for the merchant MUST be able to cancel a pending change without re-authenticating. Cancelling MUST leave the current address in effect and release held payouts to it.
- **FR-008**: Payout addresses MUST NOT be settable or changeable through API keys.

**Alerts and audit**

- **FR-009**: Each of these MUST be recorded as an audit event with the merchant, store, time, and old and new values where they apply: payout address requested, cancelled, took effect; failed or abandoned re-authentication for an address change; payout settings changed; payout sent, confirmed, failed, checking; payouts paused or resumed for the store; "Pay out now" and "Retry now" used.
- **FR-010**: A requested, cancelled or applied address change MUST show a banner on every Dashboard page for that merchant until acknowledged, and MUST send a webhook to the store's webhook URL if one is set.

**When payouts happen**

- **FR-011**: A store MUST be able to choose when DD is paid out: as soon as each payment is confirmed (the default), once a day at a time the merchant picks, or when the held confirmed DD reaches an amount the merchant sets.
- **FR-012**: With a threshold, held DD MUST still be paid out once the oldest held payment has been held for 7 days, whatever the amount.
- **FR-013**: The merchant MUST be able to pay out all confirmed held DD at any time ("Pay out now"), subject to the payout minimum.
- **FR-014**: DD MUST be paid out only once each payment it came from has at least 6 network confirmations, which can be later than the payment first shows as confirmed. Until then the Dashboard MUST show that DD as "confirming" with the reason "waiting for confirmations".
- **FR-015**: Each payout MUST be at least 1.00 DD, because the DigiDollar network can't send less. When a store is deleted, any remainder below 1.00 DD MUST move to the merchant's account-level DD balance (FR-039) rather than being lost.
- **FR-016**: Payouts MUST cover all DD DigiPay holds for the store, including confirmed payments, overpayments, late payments, extra payments in the other currency on buyer-choice payments, and donations. Amounts MUST come from what actually arrived on the network at the store's DD addresses, not only from what DigiPay expected.

**Fees**

- **FR-017**: DigiPay MUST pay the DGB network fee for every payout from its own funds. The merchant MUST receive the full DD amount held for them, with nothing deducted.
- **FR-018**: If DigiPay can't pay the fee, payouts MUST wait (not fail), the merchant MUST see "delayed by DigiPay", and the DigiPay operator MUST be alerted.

**Safety of payouts**

- **FR-019**: DD MUST never be paid out twice. When it is unclear whether a payout reached the network, it MUST be marked "checking" and MUST NOT be retried until DigiPay knows the first attempt didn't go through.
- **FR-020**: A payout MUST go only to the store's payout address in effect at the time it is sent.
- **FR-021**: DigiPay operators MUST be able to pause and resume all payouts, or one store's payouts. Paused payouts MUST wait, not fail, and affected merchants MUST see that payouts are paused and since when.
- **FR-022**: After 3 failed payouts in a row for a store, automatic payouts for that store MUST pause and the merchant MUST be asked to check the payout address. Payouts MUST resume once the merchant confirms the address (or changes it) or presses "Retry now" and it succeeds.
- **FR-023**: If a payment that was already paid out later loses its confirmation, DigiPay MUST absorb the loss: the DigiPay operator MUST be alerted, the merchant MUST see a note on the affected payment, and the store's balance and later payouts MUST NOT be reduced. DigiPay MUST make good the missing DD from its own funds, so other stores' DD is never used to cover it.

**Dashboard**

- **FR-024**: For each store with DigiDollar on, or holding DD, the Dashboard MUST show the DD held (confirmed and still confirming), the time it was last checked, the payout address in effect and any pending change, and the payout setting.
- **FR-025**: When DD is held and not being paid out, the Dashboard MUST say why and, where known, when the next payout is expected.
- **FR-026**: The Dashboard MUST list a store's payouts, newest first, with amount, destination, time, status (sending, sent, confirmed, failed, checking, waiting), transaction link, and the payments and donations each one covers, and MUST let the merchant filter by status.
- **FR-027**: A failed payout MUST show a plain reason, that the DD is still held for the merchant, and whether DigiPay will retry or needs action. The merchant MUST be able to retry it.
- **FR-028**: Each DD payment in the Dashboard MUST show whether its DD has been paid out, and link to the payout.
- **FR-029**: Held DD MUST be broken down by source: confirmed payments, overpayments, late payments, extra payments to refund, and donations. Extra payments to refund MUST be labelled as such on the payment and on the payout.
- **FR-030**: A store with DigiDollar on and no payout address MUST show a persistent prompt to add one, saying that DigiPay is holding the store's DD until then.
- **FR-031**: Turning DigiDollar on for a store MUST require a payout address to be set (or set during the same step). Stores that already had DigiDollar on when this feature ships keep working without one but get the prompt (FR-030).

**Webhooks and API**

- **FR-032**: Stores with a webhook URL MUST receive signed webhooks for payout sent, payout confirmed, payout failed, and payout address change requested, cancelled and applied, using the store's existing webhook signing, retries and delivery log.
- **FR-033**: Merchant API keys MUST be able to read a store's DD balance (with its breakdown) and payouts, read-only.

**DigiDollar service unavailable**

- **FR-034**: When the DigiDollar service is unavailable, payouts that are due MUST wait, shown as "waiting for DigiDollar service", and MUST run automatically once it is back. They MUST NOT be marked failed for this reason.
- **FR-035**: While the DigiDollar service is unavailable, the Dashboard MUST keep working, show the last known DD balance with the time it was last checked and a note that it may be out of date, and still let the merchant set or change a payout address.
- **FR-036**: DGB payments, DGB monitoring and every other DigiPay function MUST NOT depend on the DigiDollar service or on payouts.
- **FR-037**: If the DigiDollar service stays unavailable for more than 6 hours while payouts are waiting, the DigiPay operator MUST be alerted, and affected merchants MUST see that payouts are delayed.

**Held DD from before this feature**

- **FR-038**: All DD DigiPay held for a store before this feature ships MUST be shown as the store's held DD and paid out under the same rules as new DD once a payout address is in effect.
- **FR-039**: DD held for a store that was deleted before this feature ships MUST be shown to the merchant on the Dashboard, outside any store, with a way to set a payout address for it under the same security rules.

**Custody notices (with spec 033)**

- **FR-040**: Once payouts are available, the Dashboard's DigiDollar notices (033 FR-031) MUST say that DigiPay holds DD only until it is paid out to the store's payout address, and MUST no longer say the DD can't be withdrawn.

**Documentation**

- **FR-041**: The merchant docs and API docs MUST describe the payout address, the waiting period and why it exists, payout timing options, that DigiPay pays the network fee, payout statuses, the payout webhooks and the read-only payout API.

### Key Entities

- **Store DD payout address**: the store's DD address in effect, when it took effect, and who set it. At most one per store.
- **Pending payout address change**: the requested address, who requested it, when, when it takes effect, and whether it was cancelled or applied.
- **Store payout setting**: when payouts go out (as soon as confirmed, once a day at a time, or at a threshold amount).
- **Store DD balance**: DD held for a store, split into confirmed and still confirming, and by source (payments, overpayments, late payments, extra payments to refund, donations), with the time last checked. Worked out from what arrived on the network at the store's DD addresses, minus what has been paid out. A payment undone after it was paid out counts as paid out and never makes the balance negative.
- **DD payout**: one transfer of a store's DD to its payout address: amount, destination, the payments and donations it covers, status (waiting, sending, checking, sent, confirmed, failed), the reason when waiting or failed, the network transaction id, and times for each step.
- **Absorbed reversal**: an amount already paid out whose source payment was later undone, absorbed by DigiPay. Records the payment, the payout, the amount and when it was found, for the operator and for the note shown to the merchant.
- **Audit events and webhooks**: the existing records and notifications, with new event types for payout addresses and payouts.

## Dependencies

- **Sending DD from DigiPay's node**: DigiPay can currently only create DD addresses, read balances and find received payments. Payouts need DigiPay to be able to send DD from the addresses it holds and to find out whether a send reached the network. This is a new capability of the DigiDollar service DigiPay uses.
- **DigiPay's DGB fee funds**: payouts rely on the DGB DigiPay already keeps to cover DD network fees.
- **Spec 033** depends on this feature (033 FR-032). This spec does not require 033: it covers DD from API-created payments today and any DD from 033's surfaces once they ship.

## Constitution Alignment

- **Principle I (Self-custody) and II (In-browser cryptography)**: these protect wallet users' keys, and payouts don't touch them. Payouts are signed with DigiPay's own DigiDollar node keys, which are the operator's keys for funds DigiPay already holds. The custody itself comes from the current DD design, not from this feature, and this feature reduces it: by default DD stays with DigiPay only until it is confirmed. Keeping custody short is why "as soon as confirmed" is the default and the threshold option has a 7-day cap. Because the constitution doesn't speak to DigiPay holding merchant funds at all, `/speckit-plan`'s Constitution Check should record this. A later way for merchants to receive DD straight to their own keys (as DGB does) would remove the custody and is out of scope here.
- **Principle III (Test-first)**: DD address checks use the existing DD address validator. Any change to it, or any new DD transaction building in the wallet or crypto libraries, needs tests including wrong network, bad checksum, DGB address and truncated input.
- **Principle IV (Boundaries)**: payout address checks reuse the existing DD address validation rather than adding a second copy in DigiPay.
- **Principle V (Graceful degradation)**: FR-034 to FR-037 define what merchants see when the DigiDollar service is down. DGB flows never depend on it (FR-036).
- **Security & Custody — Address handling**: the payout address is checked in the Dashboard before it is shown or saved, and again before every payout (FR-002).
- **Security & Custody — Auth boundary**: address changes need a fresh Digi-ID sign-in and can't be made with API keys (FR-004, FR-008). This is an auth-relevant change and must be flagged as security-relevant in its pull request.
- **Secret hygiene**: payout logs and audit events contain addresses and amounts but never node credentials or keys.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A merchant can set a DD payout address for a store with no held DD in under 2 minutes, including the Digi-ID sign-in.
- **SC-002**: With the default setting and the DigiDollar service available, 95% of confirmed DD payments reach the merchant's payout address within 15 minutes of being confirmed.
- **SC-003**: 100% of DD a store receives (payments, overpayments, late payments, extra payments and donations) is either paid out or shown as held with a reason. DigiPay's total held DD is never less than the sum of the stores' held DD shown in the Dashboard, including after an absorbed reversal.
- **SC-004**: No DD is ever paid out twice, and no payout ever goes to an address other than the one in effect at the time.
- **SC-005**: No payout address change takes effect less than 48 hours after it is requested, except a first address on a store with nothing held.
- **SC-006**: Every address change is visible to the merchant on the Dashboard and in the audit log, and by webhook when one is set, within 1 minute of the request.
- **SC-007**: Merchants receive exactly the DD held for them: in 100% of payouts the amount received equals the amount shown, with no fee deducted.
- **SC-008**: While the DigiDollar service is down, DGB payments and the Dashboard keep working, and no payout is marked failed because of the outage.
- **SC-009**: Every store holding DD when this ships can see its full held balance, broken down by source, on first opening the Dashboard after release.

## Assumptions

- Merchants sign in to the Dashboard with Digi-ID, and DigiPay has no merchant email address, so alerts are the Dashboard banner, audit log and webhooks. Email or other alert channels are out of scope.
- The waiting period for an address change is 48 hours. It is one fixed value for all stores and not configurable by the merchant.
- Payouts are held, not sent to the old address, during a pending change. That way a merchant who lost control of their old wallet isn't forced to keep paying into it, and an attacker who changed the address can't receive anything before the merchant notices.
- The payout minimum is 1.00 DD, matching the smallest DD payment.
- "Once a day" runs at the merchant's chosen time in the merchant's chosen time zone. The time defaults to 00:00, and the time zone to the merchant's browser time zone when they set it up, otherwise UTC.
- DigiPay pays out from the addresses it holds for the store. Whether it pools or keeps per-payment addresses is a design choice for the plan, as long as each store's balance and payouts stay separate and traceable to their payments.
- Refunding a buyer's extra payment is the merchant's responsibility once the DD has been paid out to them, as it is for DGB today. DigiPay doesn't send refunds to buyers.
- DigiPay knows which store every DD address it created belongs to (each was created for one store's payment or donation address), so all held DD can be assigned to a store or merchant.
- Payout webhooks use the same signing and retry behaviour as today's payment webhooks.
- DigiDollar stays labelled beta, including payouts.
- Out of scope: merchants receiving DD straight to their own keys without DigiPay holding it, multiple payout addresses or splits per store, paying out in a different currency than DD, and automatic refunds to buyers.
