# Feature Specification: DigiDollar on every DigiPay payment surface

> **Changed by [spec 035](../035-digipay-dd-merchant-keys/spec.md)** (2026-11-27):
> - DD addresses (explicit DD payments, the buyer's DD option and the DD donation address) now come from the store's own DigiDollar key, not a DigiPay node.
> - "DD is available" comes from the public explorer's health.
> - Turning DD on needs the store's DD key, not a payout address. Spec 034's payouts are gone.

**Feature Branch**: `033-digipay-dd-everywhere`

**Created**: 2026-11-27

**Status**: Draft

**Input**: User description: "DigiPay: let buyers actually pay in DigiDollar (DD) outside the API. Today a store's 'Enable DigiDollar for this store' checkbox only takes effect for API callers that create a session with currency DD. The POS terminal only creates DGB sessions and always shows DGB on the paid screen; Dashboard payment links default to DGB; the donation button is DGB-only. When a store has DigiDollar enabled, the merchant-facing surfaces should offer DD: (1) POS till can charge in DD (USD-denominated, 1 DD = 1 USD) alongside DGB/fiat, (2) payment links can be created in DD, (3) donation button can accept DD, and the paid/terminal screens show the correct currency. When DD is disabled, nothing changes. Handle the DD node being unavailable gracefully in each surface. Also update the dashboard checkbox description so it accurately says where DD is offered."

## Background

A merchant can tick **Enable DigiDollar for this store** in the DigiPay dashboard. The setting's description says buyers will be able to pay in DD. Today that's only true when the merchant's own code creates a payment through the API with DD as the currency. The POS terminal, Dashboard payment links (and the one-off "new payment" form), and the donation button still only offer DGB. A merchant who ticks the box and then uses any of these tools never sees a DD option, and nothing tells them why.

## Clarifications

### Session 2026-11-27

- Q: If a payment is priced in a currency other than USD and the buyer switches to DD, how long does the converted DD amount stay fixed? → A: Fixed from the buyer's first switch to DD until the payment expires.
- Q: Should buyer choice at checkout switch on automatically for stores that already have DigiDollar on when this ships? → A: Yes. It's on for every store with DigiDollar on, including existing ones, and the Dashboard shows a one-time notice about the change.
- Q: Since DigiPay ends up holding the DD that buyers pay, how should the merchant actually get that money? → A: DD payouts become their own spec. This feature depends on it: DD goes to the new places only once payouts exist. Until then, the Dashboard says plainly that DigiPay holds the DD.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Charge in DigiDollar at the POS till (Priority: P1)

A cashier at a store with DigiDollar on rings up a sale in US dollars and chooses to charge it in DD. The buyer's screen shows a DD amount equal to the dollar total (1 DD = 1 USD) and a fresh DD address. Once the buyer pays, the paid screen shows the amount in DD, not DGB.

**Why this priority**: The POS is where in-person merchants take payments. A stable, dollar-pegged currency matters most at a counter, where the price shouldn't move between ringing up and paying. The checkbox promises this today and doesn't deliver it.

**Independent Test**: Turn DD on for a test store, pair a POS, enter $12.50, choose DD, and pay the shown address with 12.50 DD. The till moves to paid and shows "12.50 DD".

**Acceptance Scenarios**:

1. **Given** a store with DigiDollar on, **When** the cashier opens the till's currency choice, **Then** DD appears as an option next to DGB and the fiat currencies.
2. **Given** the till is set to DD, **When** the cashier enters 12.50 and charges, **Then** the buyer sees 12.50 DD, a DD address and a QR code, and no DGB price-drift warning.
3. **Given** a DD charge is paid, **When** the payment is detected and confirmed, **Then** the paid screen, the receipt and the shift totals show the amount in DD.
4. **Given** a store with DigiDollar off, **When** the cashier opens the currency choice, **Then** DD is not offered and the till behaves exactly as it does today.
5. **Given** the till was left on DD and the merchant later turns DigiDollar off, **When** the till next loads or charges, **Then** it falls back to the store's default currency and tells the cashier DD is no longer available.

---

### User Story 2 - Create a payment link (or one-off payment) in DigiDollar (Priority: P2)

A merchant on the Dashboard creates a payment link for an invoice and picks DD as the currency. They enter a dollar amount, get a link and QR code, and send it. The buyer opens the link and pays in DD.

**Why this priority**: Payment links are how online and remote merchants get paid without writing any code. It's the second most common way to create a payment, and DD's fixed dollar value suits invoices.

**Independent Test**: With DD on, create a $40 link in DD, open the checkout page, and confirm it asks for 40.00 DD at a DD address. Pay it and confirm the Dashboard row shows 40.00 DD as paid.

**Acceptance Scenarios**:

1. **Given** a store with DigiDollar on, **When** the merchant opens the link form's currency choice, **Then** DD is offered alongside DGB and the fiat currencies.
2. **Given** DD is chosen, **When** the merchant enters an amount, **Then** the form shows it as US dollars and DD at 1:1, with no exchange-rate lookup.
3. **Given** a DD link is created, **When** the buyer opens it, **Then** the checkout shows the DD amount and a DD address.
4. **Given** the store's default currency is DD, **When** the link form opens, **Then** DD is already selected.
5. **Given** a store with DigiDollar off, **When** the merchant opens the link form, **Then** DD isn't offered, and a default currency of DD (if one was saved earlier) falls back to USD.

---

### User Story 3 - Buyer picks DGB or DD at checkout (Priority: P2)

A merchant at a store with DigiDollar on prices a sale in a regular currency (for example $25.00 or ₱1,250) from the POS, a payment link or a one-off payment. The buyer sees the payment with a choice: pay in DGB or pay in DD. Whichever they pick, the page shows the right amount, address and QR code. The merchant gets one payment for one order, marked paid, with the currency the buyer used.

**Why this priority**: Most buyers hold one currency or the other, and the merchant often doesn't know which. Making the merchant guess at creation time loses sales. Letting the buyer choose means one link or one till charge works for everyone.

**Independent Test**: With DD on, create a $25.00 payment link. Open it, confirm it starts on DGB with a DGB amount, switch to DD and confirm it shows 25.00 DD at a DD address. Pay in DD. The Dashboard shows one payment, paid, 25.00 DD.

**Acceptance Scenarios**:

1. **Given** a store with DigiDollar on and a payment priced in USD, **When** the buyer opens checkout, **Then** they see a DGB/DD choice, starting on the store's preferred currency for checkout (DGB unless the store's default currency is DD).
2. **Given** a payment priced in USD, **When** the buyer switches to DD, **Then** the amount shown is the USD price as DD at 1:1, with a DD address and QR code.
3. **Given** a payment priced in a non-USD currency (for example ₱1,250), **When** the buyer switches to DD, **Then** the DD amount is the price converted to US dollars at the rate when they first pick DD, rounded up to the cent. The page says what rate was used, and that amount holds until the payment expires.
4. **Given** the buyer has switched currencies, **When** they switch back, **Then** they see the same amount and address they saw before for that currency (no new address each time).
5. **Given** the buyer has started paying in one currency (funds seen on the network), **When** they look at checkout, **Then** the choice is locked to that currency.
6. **Given** a POS charge priced in a regular currency, **When** the till shows the charge, **Then** the cashier or buyer can switch the on-screen QR between DGB and DD in one tap.
7. **Given** a payment the merchant created explicitly in DD, or with an amount typed in DGB, **When** the buyer opens checkout, **Then** there is no choice: it's DD-only or DGB-only, as today.
8. **Given** a store with DigiDollar off, **When** the buyer opens any checkout, **Then** it shows DGB only, exactly as today.
9. **Given** the buyer paid in DD, **When** the merchant views the payment, receipt, webhook or totals, **Then** it shows the amount paid in DD and still references the same order.

---

### User Story 4 - Accept DigiDollar donations (Priority: P3)

A merchant with DigiDollar on embeds the donation button. A supporter opening the tip page can choose to give in DGB or DD and sees the matching address and QR code.

**Why this priority**: Donations are lower in volume than sales and work differently (one reusable address, no session or monitoring). It's worth doing, but it builds on the other two stories.

**Independent Test**: With DD on, open the store's tip page, switch to DD, and confirm the page shows a DD address that stays the same across reloads. Send DD to it and see the funds arrive in the store's DD balance.

**Acceptance Scenarios**:

1. **Given** a store with DigiDollar on, **When** a supporter opens the tip page, **Then** they can switch between DGB and DD, and each choice shows its own address, QR code and unit.
2. **Given** the button was set up with a suggested amount, **When** the supporter picks DD, **Then** the suggested amount is shown in DD only if the merchant set a DD amount; otherwise no amount is shown. A DGB amount is never relabelled as DD.
3. **Given** a store with DigiDollar off, **When** the tip page opens, **Then** it shows DGB only, exactly as today.
4. **Given** a merchant wants a DD-first button, **When** they set up the button, **Then** they can make DD the currency the tip page opens on.

---

### User Story 5 - The setting says what it actually does (Priority: P2)

The merchant reading the DigiDollar setting in the Dashboard can see exactly where buyers will be offered DD once it's on.

**Why this priority**: The current text over-promises. Correcting it is cheap and stops support confusion even before the other stories ship.

**Independent Test**: Read the setting's description and compare it with where DD actually shows up. Every place listed offers DD, and no place offering DD is missing from the list.

**Acceptance Scenarios**:

1. **Given** the DigiDollar setting, **When** the merchant reads its description, **Then** it lists the places DD is offered (POS till, payment links and one-off payments, the buyer's DGB/DD choice at checkout, donation page, API) and states that 1 DD = 1 USD and that network fees are covered by DigiPay.
2. **Given** a merchant turns DigiDollar on, **When** the confirmation message appears, **Then** it says where DD is now available instead of a generic "enabled".

---

### Edge Cases

- **DD node unavailable when creating a payment** (POS, link or one-off payment): the merchant sees a plain message ("DigiDollar is temporarily unavailable, try again or charge in DGB"). The amount they typed is kept, and one tap switches the same amount to DGB or the store's fiat default. No half-created payment is left behind.
- **DD node unavailable on the tip page**: the DD option shows as unavailable with a short note, and DGB keeps working.
- **DD turned off while a DD payment is open**: payments already created in DD keep working through to paid. Turning DD off only stops new DD payments.
- **DD amount limits**: amounts below 1.00 DD or above 100,000.00 DD are rejected before submitting, with the limit shown, matching the limits the API already enforces.
- **Fiat or DGB amount when switching to DD**: switching the till or link form to DD keeps a USD amount as is. An amount in another fiat currency or in DGB is cleared rather than converted, so the merchant never charges a converted figure they didn't type.
- **More than two decimals**: DD amounts are limited to cents. Input beyond two decimals isn't accepted.
- **Underpaid or overpaid DD payment**: shown the same way as for DGB payments today, in DD units.
- **Saved till currency of DD on a device whose store has DD turned off**: falls back to the store default (see Story 1, scenario 5).
- **Buyer pays in both currencies**: once funds arrive in one currency the choice locks. If funds still arrive at the other currency's address before the payment expires, or within the 3-day late-payment window after it, they're recorded on the same payment as an extra payment for the merchant to refund, and the Dashboard flags it. They're never silently ignored.
- **DD unavailable at checkout**: the DD choice shows as unavailable with a short note. DGB keeps working, and a buyer already on DD is moved back to DGB with an explanation, unless they've already started paying in DD.
- **Converted DD amount outside the limits**: if a non-USD price converts to less than 1.00 or more than 100,000.00 DD, the DD choice isn't offered for that payment.
- **No exchange rate available for a non-USD price**: the DD choice is unavailable for that payment until a rate is available. DGB is unaffected.
- **DD turned off while a buyer is on checkout** (by the merchant or by the DD surfaces switch): the DD choice disappears on the next refresh, even if the buyer had already opened the DD option, unless the buyer has already started paying in DD, in which case that payment completes.
- **Expiry**: the payment expires at the same time whichever currency the buyer picks. Switching currencies doesn't extend it.
- **Payments open when this ships**: payments created before buyer choice existed stay DGB-only. Only payments created afterwards offer the choice.

## Requirements *(mandatory)*

### Functional Requirements

**Availability rule**

- **FR-001**: DD MUST be offered on a payment surface only when that store has DigiDollar turned on. With it off, every surface MUST look and behave exactly as it does today.
- **FR-002**: Turning DigiDollar off MUST stop new DD payments only. Payments already created in DD MUST continue to completion.

**POS till**

- **FR-003**: The POS currency choice MUST include DD for stores with DigiDollar on.
- **FR-004**: A till set to DD MUST take amounts in US dollars with two decimals and create a DD payment for that amount (1 DD = 1 USD), with no exchange-rate lookup and no price-drift warning.
- **FR-005**: The till's waiting, paid, late-payment and receipt screens and the shift totals MUST show DD payments in DD, never as DGB.
- **FR-006**: If a till's saved currency is DD and the store no longer has DigiDollar on, the till MUST fall back to the store default currency and tell the cashier why.

**Payment links and one-off payments**

- **FR-007**: The Dashboard's payment-link form and one-off payment form MUST offer DD for stores with DigiDollar on, and create a DD payment for the amount entered in dollars.
- **FR-008**: The store's default currency setting MUST allow DD when DigiDollar is on. When DigiDollar is off, a saved DD default MUST act as USD.
- **FR-009**: Payment lists, payment details and sales overviews in the Dashboard MUST show DD payments in DD and MUST NOT add DD amounts to DGB totals.

**Buyer choice at checkout**

- **FR-020**: For stores with DigiDollar on, a payment priced in a regular (fiat) currency MUST let the buyer choose to pay in DGB or DD, on the hosted checkout page and on the POS charge screen.
- **FR-021**: The choice MUST start on DD when the store's default currency is DD, and on DGB otherwise.
- **FR-022**: The DD amount MUST equal the USD price at 1:1. For any other currency it MUST be the price converted to US dollars at the rate when the buyer first picks DD, rounded up to the cent, with the rate shown. That DD amount MUST then stay fixed until the payment expires, including when the buyer switches away and back. The DGB amount MUST be worked out as it is today.
- **FR-023**: Each currency MUST get at most one address per payment. Switching back and forth MUST show the same address and amount for a currency rather than creating new ones.
- **FR-024**: Once funds for the payment are seen in one currency, the choice MUST lock to that currency and the payment MUST complete in it.
- **FR-025**: Funds that arrive in the other currency before the payment expires, or within the 3-day window DigiPay already watches for late payments, MUST be recorded on the same payment as an extra payment to refund and flagged to the merchant. Extra DD is paid out to the merchant with the store's other DD, labelled "extra payment, refund the buyer" (spec 034 FR-029); DigiPay doesn't refund buyers itself.
- **FR-026**: A payment the merchant created explicitly in DD, or with an amount typed in DGB, MUST NOT offer a choice.
- **FR-027**: A payment MUST stay one payment with one order reference whichever currency the buyer picks. The payment details, receipt, notifications to the merchant's systems and totals MUST show the currency and amount actually paid.
- **FR-028**: Payments created by a merchant's own code MUST offer the choice only when that code asks for it, so existing integrations that expect DGB keep working unchanged.
- **FR-029**: The merchant MUST be able to turn buyer choice at checkout off for a store. It MUST be on by default whenever DigiDollar is on, including for stores that turned DigiDollar on before this feature ships.
- **FR-030**: Merchants whose stores already had DigiDollar on when this feature ships MUST see a one-time Dashboard notice that buyers can now pick DGB or DD at checkout, with a direct way to turn that off. The notice MUST NOT come back once dismissed.

**Donations**

- **FR-010**: For stores with DigiDollar on, the tip page MUST let the supporter choose DGB or DD and show the matching address, QR code and unit.
- **FR-011**: Each store MUST have one DD donation address that stays the same across visits, separate from the addresses used for DD payments.
- **FR-012**: The donation button setup MUST let the merchant choose which currency the tip page opens on and give a suggested amount per currency. A DGB amount MUST NOT be shown as a DD amount.
- **FR-013**: The donation button setup in the Dashboard MUST include the DD options only when DigiDollar is on.

**Failure handling**

- **FR-014**: When DD can't be used because the DigiDollar service is unavailable, every surface MUST show a plain-language message, keep what the merchant typed, and offer a one-step way to take the payment in DGB or the store's fiat default instead.
- **FR-015**: DGB payments MUST keep working on every surface while DD is unavailable.
- **FR-016**: Amounts outside the DD limits (1.00 to 100,000.00) MUST be rejected before submitting, with the limit shown.

**Setting description**

- **FR-017**: The DigiDollar setting's description MUST list where DD is offered at that point (the API alone until payouts exist, then every surface in this spec) and MUST NOT claim more than this.
- **FR-018**: The message after turning DigiDollar on or off MUST say where DD is now offered or no longer offered.

**Custody and payouts**

- **FR-031**: Wherever the merchant turns on or manages DigiDollar, the Dashboard MUST say plainly that DigiPay holds the DD that buyers pay until it's paid out to the merchant. While payouts don't exist yet, it MUST also say the DD can't be withdrawn yet.
- **FR-032**: DD MUST NOT be offered on the POS, payment links, one-off payments, buyer choice at checkout or the donation page until DD payouts to merchants are available (see Dependencies). The notice (FR-030) MUST NOT appear until then either. FR-017 and FR-031 apply straight away, so today's API-only DD is described accurately.

**Documentation**

- **FR-019**: The merchant docs and API docs MUST describe DD on the POS, payment links and donation page, the buyer's DGB/DD choice at checkout, and how a merchant's own code asks for that choice and reads which currency was paid.

### Key Entities

- **Store DigiDollar setting**: the existing on/off switch per store. It decides whether any surface offers DD.
- **Store default currency**: existing per-store setting. DD becomes an allowed value when DigiDollar is on.
- **DD payment**: an existing payment with currency DD, a dollar amount in cents and a fresh DD address.
- **Payment with buyer choice**: one payment that can be paid in DGB or DD. It keeps its price in the original currency and, for each currency the buyer has looked at, an amount and an address (plus the rate used for a converted DD amount). It records the currency actually paid, the time the choice locked, and any extra payment received in the other currency.
- **Store checkout-choice setting**: new per-store on/off switch for buyer choice at checkout. On by default when DigiDollar is on.
- **Store DD donation address**: new. One reusable DD address per store for donations, created once and kept.
- **Donation button settings**: the existing button options, plus the currency the tip page opens on and an optional suggested DD amount.

## Dependencies

- **DD payouts to merchants** ([spec 034](../034-digipay-dd-payouts/spec.md)): merchants set a DD payout address, and DigiPay forwards their DD to it, paying the network fee. Spec 034 also requires a payout address before DigiDollar can be turned on (034 FR-031), and updates this spec's holding notice once payouts are live (034 FR-040). Today DD paid to a store goes to addresses only DigiPay controls and can't be withdrawn. Every DD surface in this spec ships only after that feature, so this spec doesn't widen the amount of merchant money DigiPay holds with no way out.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Once DD payouts are available and with DigiDollar on, a merchant can take a DD payment from the POS, a payment link, a one-off payment and the donation page, without writing any code.
- **SC-002**: A cashier can switch a till to DD and charge a sale in under 10 seconds more than a DGB sale takes today.
- **SC-003**: 100% of DD payments show DD (never DGB) on every screen a merchant or buyer sees, including receipts and totals.
- **SC-004**: With DigiDollar off, none of the surfaces show anything new. Existing behaviour and tests stay unchanged.
- **SC-005**: When the DigiDollar service is down, a merchant can still complete the sale in DGB from the same screen in one step, without re-entering the amount.
- **SC-006**: Every place the setting's description lists offers DD, and no other place does.
- **SC-007**: One fiat-priced payment link or POS charge can be paid by a buyer holding only DGB or only DD, with no new link and no merchant action.
- **SC-008**: A buyer can switch between DGB and DD at checkout in one tap, and the new amount and QR code appear within 2 seconds.
- **SC-009**: Every payment paid in DD after a buyer's choice shows up as exactly one payment for its order, with the currency paid, everywhere the merchant looks.
- **SC-010**: Existing integrations that create payments from their own code see no change in behaviour unless they ask for buyer choice.

## Assumptions

- Two ways to get a DD payment: the merchant picks DD when creating it (fixed DD), or the merchant prices it in a regular currency and the buyer picks DGB or DD at checkout. Payments with an amount typed in DGB stay DGB-only, because their price is set in DGB.
- 1 DD = 1 USD. DD amounts are entered in US dollars and stored in cents, with the API's existing limits (1.00 to 100,000.00).
- When the merchant switches the till or link form to DD, there's no conversion: only a USD amount carries over and other amounts are cleared. Conversion happens only for buyer choice on a non-USD price, at the rate in effect when the buyer first picks DD (then fixed until the payment expires), using the same price source DigiPay already uses.
- The DD address for a buyer-choice payment is created only when the buyer first picks DD, so payments paid in DGB never use up a DD address. The exception is a store whose default currency is DD: its payments start on DD, so the DD address is created with the payment.
- DD donations go to the same DigiPay-held DD funds as DD payments and are paid out the same way once payouts exist. Donations aren't watched as individual payments, matching how DGB donations work today.
- Network fees for DD stay covered by DigiPay, as the current setting already says.
- Webhooks, the API and the checkout and receipt pages already handle fixed DD payments. Buyer choice adds to them: a payment's paid currency can now differ from what was offered first, so these places must report the currency actually paid.
- DigiDollar stays labelled beta wherever it's offered.
