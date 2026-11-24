# Feature Specification: Multiple Recipients in a Multisig Payment

**Feature Branch**: `feature/multisig-multiple-recipients` (planned; spec directory `029-multisig-multiple-recipients`)

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "help me implement the multiple recipients in MultiSig, similar to what we have in the Main wallet send to Multiple recipients."

## Context

**What the main wallet does today.** The Send page has a "multiple recipients" mode (batch send). The person turns it on, gets two recipient rows to start, and can add rows up to 20. Each row has its own address (typed, pasted, scanned or picked from contacts) and its own amount. Before sending, the wallet checks every row: the address must be valid, must not repeat another row, must not be one of the wallet's own addresses, and the amount must be at least 0.0001 DGB. A summary shows the total, the fee, the grand total and what remains. Everything goes out as one transaction with one fee.

**What a shared (multisig) wallet does today.** Sending from a shared wallet creates a *payment proposal* for one recipient and one amount. The proposal is then shared with co-signers (QR code, file, copy/paste or the live signing room), each co-signer reviews and signs it, and once enough have signed it is broadcast. Every part of that journey assumes exactly one recipient: the proposal stores one address and one amount, the review screens show one recipient, and a proposal received from a co-signer that pays more than one recipient is refused ("online signing does not support" it).

**What this feature changes.** A shared-wallet proposal can pay several recipients at once, built the same way as a batch send in the main wallet, and every co-signer can see and check each recipient before signing. Single-recipient proposals keep working exactly as they do today.

## Clarifications

### Session 2026-11-25

- Q: When a co-signer reviews a multi-recipient proposal, should the proposer's contact names for the recipients be shown, or only the addresses? → A: Addresses only; a co-signer sees a name only when that address is in their own contacts. Names never travel with the proposal.
- Q: When a proposal is too large for one QR code, should it be split across several QR codes or should the wallet fall back to other sharing routes? → A: Fall back: say it is too large for a QR code and offer file, copy and the live signing room. Splitting across several QR codes is out of scope.
- Q: Should a received proposal that breaks this wallet's creation rules (more than 20 recipients, a recipient under 0.0001 DGB, a repeated address) still be accepted? → A: Accept it, but mark each rule-breaking line with a warning before signing. (Planning note: the minimum is 0.001 DGB, the floor shared-wallet payments already use; see research R2.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Propose one payment to several recipients (Priority: P1)

A co-signer of a shared wallet needs to pay several people from it (for example a treasury paying three contributors). On the shared wallet's Send page they turn on "multiple recipients", fill in an address and amount for each recipient, review the summary (each recipient, total, fee, total with fee, remaining balance) and create one proposal for all of them.

**Why this priority**: This is the feature. Without it, paying N recipients means N proposals, N rounds of signatures from every co-signer, and N fees.

**Independent Test**: In a test 2-of-3 shared wallet with funds, create a proposal with three recipients and check that one proposal exists whose payment pays each address its amount, with the change returning to the shared wallet.

**Acceptance Scenarios**:

1. **Given** a shared wallet with enough funds, **When** the person turns on multiple recipients, **Then** two empty recipient rows appear and the single-recipient form is hidden.
2. **Given** multiple-recipients mode with fewer than 20 rows, **When** the person adds a recipient, **Then** a new empty row appears, numbered after the last one.
3. **Given** three or more rows, **When** the person removes one, **Then** it disappears and the others keep their values; with only two rows, the remove control is not offered.
4. **Given** every row holds a valid address and amount, **When** the person reviews, **Then** they see each recipient (contact name when known, otherwise the address) with its amount, plus the total, the network fee, the total including the fee and the balance left in the shared wallet.
5. **Given** the reviewed payment, **When** the person creates the proposal, **Then** one proposal is saved that pays every recipient its amount in one transaction and needs the shared wallet's usual number of signatures.
6. **Given** a row with an invalid address, a duplicate address, the shared wallet's own address, a missing amount or an amount below 0.001 DGB, **When** the person tries to review, **Then** that row shows what is wrong and no proposal can be created until it is fixed.
7. **Given** recipients whose total plus fee exceeds what the shared wallet can spend, **When** the person tries to review, **Then** they are told the shared wallet does not have enough and by how much.

---

### User Story 2 - Co-signers review and sign a multi-recipient proposal (Priority: P1)

Another co-signer receives the proposal (by QR code, file, pasted text or the live signing room). Before signing, their wallet shows every recipient and amount, the total, the fee and the change, read from the payment itself, so they know exactly what they are approving. They sign, and once enough co-signers have signed the payment can be broadcast.

**Why this priority**: A proposal nobody else can open, check or sign has no value; this story makes User Story 1 usable. It is also the security core: a co-signer must never approve a recipient they were not shown.

**Independent Test**: Create a three-recipient proposal on one device, import it on a second co-signer's device by each sharing route, and check that all three recipients and amounts appear, that signing works, and that the fully signed payment broadcasts.

**Acceptance Scenarios**:

1. **Given** a multi-recipient proposal shared by QR code, file or pasted text, **When** a co-signer imports it, **Then** it is accepted and listed among the shared wallet's pending payments.
2. **Given** a multi-recipient proposal in the live signing room, **When** a co-signer joins, **Then** they can review and sign it there as they can a single-recipient one.
3. **Given** a multi-recipient proposal awaiting signatures, **When** any co-signer opens it, **Then** every recipient address and amount is shown, along with the total sent, the fee and the change returning to the shared wallet.
4. **Given** a received proposal whose saved recipients or amounts differ from what its payment actually does, **When** it is opened, merged or signed, **Then** the wallet refuses it with a plain-language reason, exactly as it does for a single-recipient mismatch today.
5. **Given** a multi-recipient proposal with enough signatures, **When** it is broadcast, **Then** every recipient is paid in one transaction and the proposal is marked sent.
6. **Given** a received proposal that has a recipient under 0.001 DGB or an address listed twice, **When** a co-signer opens it, **Then** it is accepted, each such line shows a warning, and the co-signer can still sign.

---

### User Story 3 - See multi-recipient payments in pending lists and activity (Priority: P2)

In the pending-payments list and in the shared wallet's activity, a multi-recipient payment reads clearly as one payment to several recipients (for example "Sent 150 DGB to 3 recipients"), with the full list of recipients available on its detail screen.

**Why this priority**: Useful for clarity and record-keeping, but the payment can be created, signed and sent without it.

**Independent Test**: With one pending and one sent multi-recipient payment, check that the pending list, the pending detail and the activity list each show the total and the recipient count, and that the detail screens list every recipient.

**Acceptance Scenarios**:

1. **Given** a pending multi-recipient proposal, **When** the person views the pending list, **Then** its card shows the total and the number of recipients instead of a single address.
2. **Given** a sent multi-recipient payment, **When** the person views the shared wallet's activity, **Then** it appears once, with the total sent and the number of recipients.
3. **Given** a single-recipient proposal or payment, **When** it is shown anywhere, **Then** it looks exactly as it does today.

---

### User Story 4 - Fill recipient rows quickly (Priority: P3)

As in the main wallet, each row can be filled by pasting, scanning a QR code or payment link (which may carry an amount), or picking a saved contact.

**Why this priority**: Saves time and typing mistakes, but typing every address still works.

**Independent Test**: Fill one row by contact, one by scanning a payment link with an amount, and one by pasting, and check that each row receives the right address (and amount, when the link has one) without touching the other rows.

**Acceptance Scenarios**:

1. **Given** multiple-recipients mode, **When** the person scans a payment link for row 2, **Then** row 2 gets its address and, if present, its amount, and no other row changes.
2. **Given** a saved contact, **When** the person picks it for a row, **Then** that row shows the contact's name and address.

---

### Edge Cases

- **Turning the mode off with rows filled**: the filled rows are kept while the page stays open, so turning multiple recipients back on restores them; nothing typed is silently lost.
- **Sharing size**: a payment with many recipients is larger. If it is too large to share as one QR code, the wallet says so and offers the file, copy and live signing room options instead of showing a QR code that cannot be scanned.
- **Co-signer on an older app version**: an older wallet refuses multi-recipient proposals. The proposer is told, when creating one, that every co-signer needs an up-to-date wallet to open it.
- **Funds change while waiting**: if the coins a proposal spends are spent elsewhere before it is signed, it is flagged as no longer able to complete, as single-recipient proposals already are.
- **"Max" and quick-amount shares**: these send everything (or a share) to one recipient and are not offered per row in multiple-recipients mode.
- **Memo**: one optional memo applies to the whole payment, as on the main Send page.
- **Fee**: the fee is estimated for the whole payment (all recipient outputs plus change) and updates as rows are added, removed or changed.
- **Same address with different letter case**: counts as a duplicate.
- **Proposal made by another tool**: one with more than 20 recipients, a recipient under 0.001 DGB or a repeated address is accepted, and each such line shows a warning before signing (FR-013a).
- **Leftover change too small to keep**: handled the same way as single-recipient proposals today (added to the fee and shown in the review).

## Requirements *(mandatory)*

### Functional Requirements

**Creating**

- **FR-001**: The shared wallet's Send page MUST offer a "multiple recipients" mode alongside the existing single-recipient form, matching the main wallet's batch send in how it is turned on and explained.
- **FR-002**: Multiple-recipients mode MUST start with two recipient rows, allow adding rows up to 20 and removing rows down to 2.
- **FR-003**: Each row MUST accept an address (typed, pasted, scanned or chosen from contacts) and an amount; scanning a payment link MUST fill only that row.
- **FR-004**: Before review, every row MUST be checked: valid address for the current network; not repeating another row (ignoring letter case); not the shared wallet's own address; amount present and at least 0.001 DGB, the minimum single-recipient multisig payments already use. Each problem MUST be shown on its row.
- **FR-005**: The wallet MUST check that the total of all amounts plus the estimated fee fits in what the shared wallet can spend, and say so plainly when it does not.
- **FR-006**: The fee estimate MUST cover the whole payment and MUST respect the fee-rate choice the shared wallet's Send page already offers.
- **FR-004a** *(added after implementation)*: Typed amounts, in the single-recipient form and in every row, MUST be read like the main Send page's (spec 028): a comma or a point is the decimal mark, and an amount such as "1.234" asks which was meant. An amount the page fills in itself (a payment link, a share of the balance, Max) or one the person already picked MUST NOT ask again.
- **FR-007**: The review MUST list every recipient (contact name when known, plus address) and amount, then the total, the fee, the total including the fee and the remaining balance.
- **FR-007a** *(added after implementation)*: In multiple-recipients mode the total MUST show its approximate value in the chosen currency when a price is known, as the single-recipient form does.
- **FR-008**: Creating the proposal MUST produce one payment that pays every recipient its exact amount, returns any change to the shared wallet, and requires the shared wallet's usual number of signatures.
- **FR-008a** *(added after implementation)*: Recipient addresses MUST be saved the way the transaction spells them (a bech32 address typed in capitals is saved in lower case), and the check in FR-012 MUST compare addresses in that standard form, so proposals saved before this rule still match their payment.
- **FR-009**: The single-recipient flow, including "Max", quick-amount shares and recipients arriving from the menu's Scan, MUST keep working unchanged.

**Reviewing, signing and sending**

- **FR-010**: A multi-recipient proposal MUST be shareable and importable by every route a single-recipient one uses today: QR code, file, copied text and the live signing room.
- **FR-011**: Every co-signer's wallet MUST read the recipients, amounts, fee and change from the payment itself and MUST show all of them before signing.
- **FR-011a**: Recipient names MUST NOT travel with a proposal. On any co-signer's device, a recipient is shown by its address, plus a name only when that address is in that device's own contacts.
- **FR-012**: When the recipients or amounts saved with a proposal differ from what its payment actually does, the wallet MUST refuse it with a plain-language reason, as it does for single-recipient proposals.
- **FR-013**: The existing safeguards for received proposals MUST apply unchanged: signatures only from the shared wallet's co-signers, spent-coin detection, and checking that a received copy is the same payment.
- **FR-013a**: The creation rules in FR-002 and FR-004 apply only to proposals created on this device. A received proposal that breaks them (more than 20 recipients, a recipient under 0.001 DGB, an address listed more than once) MUST still be accepted, and each line that breaks a rule MUST carry a plain-language warning shown before signing. Warnings never block signing.
- **FR-014**: A fully signed multi-recipient payment MUST broadcast and be recorded as sent, as single-recipient payments are.
- **FR-015**: When a proposal is too large for one QR code, the wallet MUST NOT show a QR code; it MUST say the proposal is too large for a QR code and offer the file, copy and live signing room routes instead. Splitting a proposal across several QR codes is out of scope.
- **FR-016**: When creating a multi-recipient proposal, the wallet MUST tell the proposer that co-signers need an up-to-date wallet to open it.

**Showing**

- **FR-017**: The pending list, the pending detail, the signing room and the shared wallet's activity MUST show a multi-recipient payment as one payment with its total and recipient count, and the detail screens MUST list every recipient and amount.
- **FR-018**: Proposals saved before this feature (one recipient) MUST keep loading and showing exactly as before.
- **FR-019**: All new text MUST be available in English and Spanish.
- **FR-019a** *(added after implementation)*: Each row's address and amount fields MUST have their own names for screen readers ("Recipient 2", "Recipient 2 · Amount"), so rows don't all read the same.

### Key Entities

- **Payment proposal (shared wallet)**: a payment awaiting co-signer signatures. Today it holds one recipient and amount; it now holds a list of one or more recipients, plus the fee, the memo, the signatures collected and its status. Its total sent is the sum of the recipients' amounts.
- **Recipient line**: one destination of a proposal: an address and an amount. A contact name is never part of it; each device shows a name only from its own contacts. Lines are kept in the order the proposer entered them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A co-signer can create a proposal paying 5 recipients from a shared wallet in under 3 minutes, using one proposal instead of five.
- **SC-002**: Paying N recipients needs one round of signatures from each co-signer instead of N rounds, and one network fee instead of N.
- **SC-003**: In testing, 100% of multi-recipient proposals shared by each route (QR code when the proposal fits in one, file, copied text, signing room) open on another co-signer's device showing every recipient and amount identical to what the proposer entered.
- **SC-004**: 100% of tampered proposals (a recipient or amount changed relative to the payment) are refused before signing.
- **SC-005**: Every existing single-recipient multisig scenario passes unchanged, including proposals saved before this feature.
- **SC-006**: A person who has used batch send in the main wallet can use the multisig version without help, since its controls, limits and checks behave the same way.

## Assumptions

- The recipient limits match the main wallet's batch send (2 to 20 recipients). The per-recipient minimum is the shared wallet's existing 0.001 DGB, not the main wallet's 0.0001 DGB, so single and multiple recipients follow one rule on the same page.
- One memo and one fee rate apply to the whole payment.
- "Max" and quick-amount shares stay single-recipient only, as the main Send page's batch mode has no "Max" either.
- Older wallet versions cannot open multi-recipient proposals; a notice to the proposer is enough, and no compatibility workaround is planned.
- The live signing room relays the payment as public data, as today; the room relay needs no change beyond carrying a larger payment (constitution Principle II).
- Only DGB is sent; DigiDollar and other assets are out of scope.
