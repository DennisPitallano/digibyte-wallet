# Feature Specification: DigiDollar on the merchant's own keys, without a node

**Feature Branch**: `035-digipay-dd-merchant-keys`

**Created**: 2026-11-27

**Status**: Draft

**Input**: User description: "DigiPay: DigiDollar without a node, on the merchant's own keys. Replace DigiPay's node-wallet DigiDollar (DD) design with a non-custodial one. A store gives its DigiDollar account public key (the BIP86 Taproot account key at m/86'/20'/0' from the merchant's DigiByte Wallet); DigiPay derives a fresh DD receive address per payment from it (as it already does for DGB with a store xpub), and watches DD arrivals and confirmations on the public Esplora explorer instead of a DigiDollar node. DD goes straight to the merchant; DigiPay never holds DD or private keys. This supersedes spec 034 and changes spec 033. The DigiByte Wallet gains a way to show and copy the DigiDollar account public key for DigiPay. No node, no SPV changes. Production has no DD node today, so nothing live needs migrating. The open PR #131 (033 + 034, unmerged) gets reworked before merge."

## Background

Specs 033 and 034 assumed DigiPay runs a DigiDollar node with its own wallet. DD paid to any store landed in that one shared wallet, and spec 034 then had to pay it out to each merchant, which brought a ledger, a payout worker, payout addresses with a 48-hour wait, a Digi-ID re-sign-in to change them, an operator key, and a constitution amendment for DigiPay holding merchant money.

DigiPay doesn't run that node in production, and the project doesn't want to. It doesn't need to:

- A DigiDollar receive address is derived from a public key, the same way a DGB address is. DigiPay already makes a fresh DGB address per payment from a store's account public key, without ever seeing a private key.
- DD arrivals and confirmations can be read from the public DigiByte explorer, which the DigiByte Wallet already uses for its own DigiDollar balance and history.

So DigiPay can take DigiDollar the same way it takes DGB: straight to the merchant's own wallet, with nothing held by DigiPay. That makes spec 034 unnecessary and simplifies spec 033.

## Clarifications

### Session 2026-11-27

- Q: Which DigiDollar account should a store's key come from? → A: The wallet's main DD account (`m/86'/20'/0'`), with the same privacy as DGB xpub mode today; no dedicated or per-store DD account.
- Q: How should the merchant's wallet find DD paid to DigiPay addresses far past its 20-unused-address scan window? → A: The wallet's DD scan gets the same "look deeper" option DGB already has (up to 500 addresses), and the key screen points merchants to it. DigiPay never reuses addresses.
- Q: Should DigiPay rely on one public explorer for DD, or fall back to a second one when it's down? → A: One configurable explorer per network, with no fallback. When it's down, DD waits and DGB keeps working.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect the store's DigiDollar key (Priority: P1)

A merchant opens the DigiDollar setting for a store, copies their DigiDollar account key from their DigiByte Wallet, and pastes it into DigiPay. DigiPay checks it, shows the first DigiDollar address it will use, and the merchant confirms it matches the one their wallet shows. Only then can DigiDollar be turned on for the store.

**Why this priority**: Without the merchant's key there is nowhere for DD to go. It replaces both the node and the payout address.

**Independent Test**: Paste a valid testnet DD account key into a testnet store, confirm the first address against the wallet, turn DigiDollar on. Paste a DGB account key or a mainnet key instead and see each refused with a plain reason.

**Acceptance Scenarios**:

1. **Given** a store without a DD key, **When** the merchant tries to turn DigiDollar on, **Then** DigiPay asks for the store's DigiDollar key first and doesn't turn it on.
2. **Given** a valid DD account key for the store's network, **When** the merchant pastes it, **Then** DigiPay shows the first DD receive address it will derive and asks the merchant to confirm it matches their wallet.
3. **Given** the merchant confirms, **When** they save, **Then** the key is stored for the store and DigiDollar can be turned on.
4. **Given** a key for the other network, a DGB key, a private key, or text that isn't a key, **When** the merchant pastes it, **Then** it's refused with a plain reason and nothing is saved.
5. **Given** a store with a DD key, **When** the merchant replaces it, **Then** new DD payments use the new key, and payments already created keep the addresses they were given.

---

### User Story 2 - DigiDollar goes straight to the merchant (Priority: P1)

Every place spec 033 offers DigiDollar keeps working, but each DD address now comes from the store's own key and is watched on the public explorer:

- explicit API payments with currency DD
- the POS DD till and DD payment links and one-off payments
- the buyer's DD option on a fiat-priced payment
- the store's DD donation address

DD lands in the merchant's own wallet the moment the buyer pays. DigiPay never holds it.

**Why this priority**: This is the point of the change: DigiDollar without custody and without a node.

**Independent Test**: On a testnet store with a DD key, create a $5.00 DD payment, pay it from another wallet, and watch it go to paid and confirmed in DigiPay. The DD shows in the merchant's own DigiByte Wallet, with no payout step.

**Acceptance Scenarios**:

1. **Given** a store with DD on, **When** a DD payment is created (API, POS, link or one-off), **Then** it gets a new DD address derived from the store's key, never one used before.
2. **Given** a buyer-choice payment, **When** the buyer first picks DD, **Then** its DD option gets a new address derived from the store's key.
3. **Given** a store with DD on, **When** the merchant first asks for the DD donation address, **Then** it's derived from the store's key, kept for the store, and the same every time after.
4. **Given** DD sent to a payment's address, **When** the explorer shows it, **Then** the payment moves through seen, paid and confirmed as it does today, in DD.
5. **Given** a DD payment is paid, **When** the merchant opens their DigiByte Wallet, **Then** the DD is in it, with no payout and nothing held by DigiPay.
6. **Given** a buyer-choice payment locked to DGB, **When** DD also arrives on its DD option, **Then** it's flagged as an extra payment as today, and the merchant refunds it from their own wallet, where it already is.

---

### User Story 3 - Show the DigiDollar key in the wallet (Priority: P2)

In the DigiByte Wallet, a merchant can show and copy their DigiDollar account key for DigiPay, with a short explanation of what it does and doesn't allow. The wallet also shows the first DD receive address, so the merchant can compare it with what DigiPay shows.

**Why this priority**: Merchants need an easy, safe way to get the key, and the address check needs the wallet's side of it.

**Independent Test**: In a wallet with DigiDollar, open the DigiPay key screen, copy the key and see the first address. Paste the key into DigiPay and see the same first address.

**Acceptance Scenarios**:

1. **Given** an unlocked wallet, **When** the merchant opens "DigiDollar key for DigiPay", **Then** the wallet shows the account key with a copy button and the first DD receive address it belongs to.
2. **Given** the key screen, **When** the merchant reads it, **Then** it says plainly that the key lets a service create receive addresses and see payments to them, but never spend.
3. **Given** a watch-only wallet, or a wallet with no DigiDollar, **When** the merchant looks for the key, **Then** it's either shown (if the wallet has the account key) or the wallet explains why it can't be shown.
4. **Given** DigiPay has given out many DD addresses, some never paid, **When** the merchant's wallet looks for DD, **Then** it still finds DD paid to any of them, as it already does for DGB beyond the usual address gap.

---

### User Story 4 - When the explorer can't be reached (Priority: P2)

If the public explorer is down or slow, DD payments wait instead of failing, the merchant and buyer see a plain message, and DGB keeps working. When the explorer is back, waiting DD payments catch up.

**Why this priority**: The explorer replaces the node as the one outside service DD depends on (constitution Principle V).

**Independent Test**: Block the explorer, create and pay a DD payment, see "waiting for the DigiDollar service" and DGB payments still working. Unblock it and see the DD payment move to paid.

**Acceptance Scenarios**:

1. **Given** the explorer can't be reached, **When** a buyer is on a buyer-choice checkout, **Then** DD is shown as unavailable and DGB works.
2. **Given** the explorer can't be reached, **When** DD arrives on a payment, **Then** the payment keeps waiting and is updated once the explorer answers again, including after the payment would have expired, within the existing late-payment window.
3. **Given** the explorer can't be reached, **When** a merchant creates a DD payment, **Then** it's still created (the address needs no outside service) and the merchant is told that confirmations may be delayed.

---

### User Story 5 - No custody, no payouts (Priority: P3)

Everything that existed only because DigiPay held DD goes away, and every word that said DigiPay holds DD is replaced by "DD goes straight to your wallet".

**Why this priority**: The custodial machinery is unnecessary and risky to keep, but removing it has no user-facing benefit on its own.

**Independent Test**: Search the Dashboard, docs, API reference and webhooks for payouts, payout addresses, "DigiPay holds", operator pause and the step-up re-sign-in: none remain. A store turns DigiDollar on with its DD key alone.

**Acceptance Scenarios**:

1. **Given** the Dashboard, **When** a merchant manages DigiDollar, **Then** there's no payouts section, payout address, payout schedule or "DD from deleted stores".
2. **Given** the docs and setting text, **When** a merchant reads about DigiDollar, **Then** they say DD goes straight to the merchant's wallet, like DGB.
3. **Given** the API and webhooks, **When** an integrator reads them, **Then** there are no payout routes, payout events or operator routes.
4. **Given** a store being deleted, **When** it had DD on, **Then** it's deleted like any other store: there's no held DD to pay out first.

---

### Edge Cases

- **The wrong kind of key**: an account key from another purpose (the DGB account, for example) has the same shape as the DD account key, so DigiPay can't tell them apart from the key alone. The first-address check (Story 1, scenario 2) is what catches it: if the addresses don't match, the merchant doesn't confirm.
- **A key from the other network**: refused (Story 1, scenario 4).
- **A private key pasted by mistake**: refused, never stored or logged, and the merchant is told never to share it.
- **Replacing the key while payments are open**: open payments keep their addresses and are still watched, so they still complete.
- **Address reuse**: each payment gets a new index, and indexes are never reused, even for payments that expired unpaid. The donation address has its own reserved index.
- **The explorer disagrees with itself, or a payment is reorganised out**: the payment status follows what the explorer shows now, as DGB payments follow their explorer today.
- **Underpaid or overpaid DD**: handled as today for DD payments, in DD.
- **DD addresses the wallet hasn't looked at**: DigiPay may give out many addresses that are never paid. The wallet's deeper DD scan finds DD on any of them up to 500 addresses past the last paid one (Story 3, scenario 4). If a store gives out more than 500 DD addresses in a row with none paid, the Dashboard warns the merchant to run the deeper scan after the next payment arrives.
- **Personal and store DD in one wallet**: a store's key is the wallet's main DD account, so anyone with the key can see all DD in that account, as with DGB xpub mode today. The wallet's key screen says so.
- **Stores with DD on before this ships**: none in production (no node exists). Development and testnet stores without a DD key have DigiDollar turned off until a key is added.

## Requirements *(mandatory)*

### Functional Requirements

**The store's DD key**

- **FR-001**: A store MUST be able to have one DigiDollar account key, set from the Dashboard.
- **FR-002**: The key MUST be an account-level public key for the store's network. A key for the other network, a private key, or anything else MUST be refused with a plain reason. A private key MUST never be stored, logged or echoed back.
- **FR-003**: Before saving, the Dashboard MUST show the first DD receive address the key gives and ask the merchant to confirm it matches the address their wallet shows.
- **FR-004**: DigiDollar MUST be turnable on for a store only when it has a DD key. This replaces spec 034's payout-address requirement.
- **FR-005**: Replacing a store's DD key MUST affect only new DD addresses. Open payments keep their addresses and are still watched.
- **FR-006**: Setting, replacing and removing a store's DD key MUST be recorded in the audit log.

**Addresses**

- **FR-007**: Every DD address DigiPay gives out MUST be derived from the store's DD key: explicit DD payments, POS and link DD payments, the buyer's DD option, and the store's DD donation address.
- **FR-008**: Each DD payment and each DD option MUST get a new address index. Indexes MUST never be reused.
- **FR-009**: The DD donation address MUST use one index reserved for it, so it's the same every time and never given to a payment.
- **FR-010**: Creating a DD payment or DD option MUST NOT depend on any outside service.

**Watching**

- **FR-011**: DD arrivals and confirmations MUST be read from the public DigiByte explorer's DigiDollar data, not from a DigiPay node.
- **FR-012**: DD payments MUST follow the same states as today: pending, seen, paid, confirmed, underpaid, expired, and the 3-day late window.
- **FR-013**: Buyer-choice locking and extra payments (spec 033 FR-024, FR-025) MUST work the same with DD watched on the explorer.
- **FR-014**: When the explorer can't be reached, DD payments MUST wait (never fail), and MUST be updated once it answers again. DGB payments MUST NOT depend on it.
- **FR-015**: "DD is available" (spec 033 FR-014 and the checkout and tip page) MUST come from whether the explorer is reachable, not a node.
- **FR-016**: DigiPay MUST use one explorer per network (mainnet and testnet), with its address configurable. There's no fallback explorer.

**Custody removed**

- **FR-017**: DigiPay MUST NOT hold DigiDollar or any private key for it.
- **FR-018**: Spec 034's custodial parts MUST be removed: the DD ledger, payout worker, payout addresses and their 48-hour changes, the payout re-sign-in, operator pause, payout webhooks, the payouts section, DD from deleted stores, and the final payout on store deletion.
- **FR-019**: Every Dashboard, docs and API text that says DigiPay holds DD MUST instead say DD goes straight to the merchant's wallet.
- **FR-020**: The DD surfaces switch (spec 033 FR-032) MUST no longer wait for payouts. It's turned on in production once this feature is live there.

**Wallet**

- **FR-021**: The DigiByte Wallet MUST let the user show and copy their DigiDollar account key for DigiPay, together with the first DD receive address it gives.
- **FR-022**: The wallet MUST explain that the key lets a service create receive addresses and see payments to them (including all DD in the wallet's DigiDollar account), and can't spend.
- **FR-023**: The wallet's DigiDollar scan MUST have the same deeper search DGB already has, reaching 500 addresses past the last paid one, and the key screen MUST tell merchants to use it if DD paid through DigiPay seems missing.

**Docs**

- **FR-024**: The merchant docs, API reference, setting text, quickstarts and changelog MUST describe the merchant-key design. Specs 033 and 034 MUST be marked as changed or superseded by this one.

### Key Entities

- **Store DD key**: the store's DigiDollar account public key, its network, when it was set, and the next unused address index.
- **DD address** (on a payment, a buyer's DD option or the store's donation address): the address and the index it was derived at. The donation address has a fixed reserved index.
- **Removed**: spec 034's DD ledger entries, payouts, payout items, payout address changes, step-up grants, merchant-level DD settings and operator settings.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A merchant can connect a store's DD key and take their first DD payment in under 5 minutes, without contacting anyone.
- **SC-002**: 100% of DD paid through DigiPay arrives in the merchant's own wallet. DigiPay holds none, at any time.
- **SC-003**: DigiPay runs no DigiDollar node, and taking DD needs no DigiPay-operated service beyond the API and website.
- **SC-004**: A DD payment shows as seen within 1 minute of the buyer sending it, whenever the explorer is reachable.
- **SC-005**: While the explorer is unreachable, 100% of DGB payments keep working, and every DD payment paid during the outage is shown as paid within 5 minutes of it coming back.
- **SC-006**: No text in the Dashboard, docs or API reference says DigiPay holds DigiDollar or pays it out.

## Assumptions

- The DD account key is the BIP86 Taproot account key at `m/86'/20'/0'` for mainnet (and the testnet coin type for testnet), the wallet's main DigiDollar account, which it already uses. There's no dedicated or per-store DD account. DD receive addresses are derived from it on the receive chain.
- The public explorer (digiexplorer.info, which the wallet already uses) exposes the DigiDollar data needed: outputs and confirmations by address. DigiPay reuses the wallet's existing explorer client.
- Taking DD without custody needs no change to the DigiDollar protocol, NodeApi or the SPV gateway.
- Production has never run a DigiDollar node, so no store holds DD with DigiPay today, and nothing needs migrating or paying out.
- PR #131 (specs 033 and 034, unmerged) is reworked to this design before it's merged. Spec 034's code is removed from it, not merged and then deleted.
- The wallet's existing deeper address search for DGB (500 addresses) can be applied to the DD receive chain, whose scan today stops at 20 unused addresses.
- Payment privacy is the same as DGB today: anyone with the store's key could see its DD payments, so the key is treated as sensitive, though it can't spend.
