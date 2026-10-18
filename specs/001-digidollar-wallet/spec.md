# Feature Specification: DigiDollar in the DGB Wallet

**Feature Branch**: `001-digidollar-wallet`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "Add DigiDollar support to the DGB Wallet so users can hold, receive, and send USD-denominated DigiDollar alongside DGB"

## Clarifications

### Session 2026-09-27

- Q: Should this feature specification include mint (create new DigiDollar by locking DGB collateral) and redeem (unlock DGB by returning DigiDollar) flows, or defer them to a follow-up specification? → A: Defer mint/redeem to a follow-up spec; this spec covers hold / receive / send only.
- Q: For the first release of Send, should the wallet sign DigiDollar transactions on the user's device using keys derived from their existing wallet seed (non-custodial), or should a wallet-operated service hold the keys and sign on the user's behalf (custodial)? → A: Non-custodial from day one; the wallet signs on-device with keys derived from the user's existing seed and the operator never holds keys.
- Q: When the DigiDollar network is in a degraded operating mode (stale oracle price or network-reported ERR state), should the wallet still permit outbound sends with an extra warning, or block sends entirely until the situation clears? → A: Allow sends and require an extra explicit warning line in the send confirmation dialog naming the degraded state.
- Q: Does the first release of DigiDollar in the wallet ship on all three networks the wallet already supports (mainnet, testnet26, regtest), or only on mainnet with testnet/regtest coming later? → A: Mainnet and testnet26 in v1; regtest support deferred to a follow-up patch.
- Q: Should the wallet get the DigiDollar per-transaction minimum and per-transaction maximum from the network at runtime, or hardcode them as constants in the wallet's code? → A: Hardcode the current protocol limits ($1.00 minimum, $100,000 maximum per transaction) as constants in the wallet; if the protocol ever revises them, ship a patch release.

### Session 2026-09-28

- Q: Which wallet types should get DigiDollar in v1, given that DigiDollar keys are derived from the wallet's recovery phrase? → A: Recovery-phrase (HD) wallets only; private-key (WIF), watch-only, and multisig wallets show "DigiDollar requires a wallet created from a recovery phrase".
- Q: How should the team measure SC-005 when FR-023 forbids analytics that could be tied to an individual user? → A: Measure with anonymous aggregate counts only: DigiDollar section views as a share of all wallet home-screen views over the first 30 days, target ≥ 30%, with no per-user or per-device identifier.

### Session 2026-10-19

- Q: When a user restores a wallet from their recovery phrase on a new device, should the wallet automatically search for previously used DigiDollar addresses, or only show addresses generated on that device? → A: Scan automatically (on restore and on first DigiDollar open), walking derived addresses in order until 20 consecutive unused ones are found, and rebuild the address list and balance from that.
- Q: If a DigiDollar send is submitted but never confirms, what should the wallet do with its "Pending" entry and reserved funds? → A: Keep it "Pending" (funds reserved) while the node still reports it in the mempool or confirmed; if it is absent from both for 30 minutes, mark it "Failed", release the reserved funds, and notify the user.
- Q: Should users be able to send DigiDollar while the backend is unreachable? → A: No. Send is disabled with a "cannot reach network" message and there is no offline queueing; balance and history show last cached values, labeled as cached.
- Q: Which DigiDollar data may be kept in on-device storage, and how is it protected? → A: The address list, cached history and pending-send records are stored encrypted (readable only after the wallet PIN unlock, same protection as the seed); no private keys or seed material are ever stored in them.
- Q: When the view switch is off but the user still holds DigiDollar, what should they see? → A: All DigiDollar UI is hidden except a single neutral home-screen line, "DigiDollar is temporarily unavailable", shown only if the device has previously used DigiDollar; funds remain on-chain and reappear when the switch is on.
- Q: If a send would leave a DigiDollar change amount below the $1.00 per-output minimum, what should the wallet do? → A: Block the send inline, explain that the remainder would be under $1.00 (send the full balance or leave at least $1.00), and offer a "Send all" action that sends the entire spendable balance.
- Q: Should the user be able to choose the DGB network fee for a DigiDollar send? → A: No. The fee is calculated automatically (never below the protocol minimum), shown before signing, and no fee control is offered.
- Q: When the oracle has no usable price at all, what should the wallet show for the DGB USD equivalent and DigiDollar pages? → A: Hide the DGB USD equivalent, show "Price unavailable" with a non-blocking banner on DigiDollar pages; DigiDollar balance, receive, and send keep working.
- Q: When the view switch is on but DigiDollar is not yet activated on the connected network, what should the wallet show? → A: Treat it as unavailable: show "DigiDollar is not active on this network yet" with no receive or send actions, and recheck automatically on refresh.
- Q: When the oracle has no usable price at all, should the send confirmation dialog show the same extra degraded-state warning as for a stale price? → A: Yes. The dialog adds a warning line naming "price unavailable"; sending is still allowed.

## User Scenarios & Testing *(mandatory)*

Each user story below is a standalone slice. Shipping only P1 already gives users a working "hold and receive DigiDollar" wallet; P2 adds outbound sending; P3 layers on trust and price context. Every story is independently developable, testable, and demonstrable.

### User Story 1 - Hold and receive DigiDollar (Priority: P1)

A DigiByte Wallet user opts into DigiDollar, sees a DigiDollar section next to their DGB balance, generates a receive address, shares it, and watches an incoming DigiDollar payment show up in their history and balance.

**Why this priority**: Without a way to hold and receive, the wallet has no DigiDollar surface at all. This is the minimum viable slice — a merchant, friend, or exchange can already pay the user in DigiDollar, and the user can verify it landed. Every other DigiDollar feature depends on this being real.

**Independent Test**: Enable the DigiDollar feature for a test wallet, generate a receive address, send DigiDollar to that address from any external source (CLI, another wallet, a DigiPay checkout), and verify the balance, transaction history entry, and confirmation count appear correctly within one minute.

**Acceptance Scenarios**:

1. **Given** a wallet user with the DigiDollar feature enabled, **When** they open the wallet home screen, **Then** they see a DigiDollar section showing their current DigiDollar balance in US dollars alongside their DGB balance.
2. **Given** the DigiDollar feature is disabled for a user, **When** they open the wallet, **Then** no DigiDollar UI is visible (other than the neutral "temporarily unavailable" line on devices that have previously used DigiDollar, per FR-002) and the existing DGB experience is unchanged.
3. **Given** a user is on the DigiDollar page, **When** they choose "Receive DigiDollar", **Then** the wallet gives them a fresh DigiDollar address plus a scannable code, and remembers the address as one of their own.
4. **Given** a DigiDollar address the user owns has just been paid, **When** the wallet next refreshes (automatic within thirty seconds or a manual pull-to-refresh), **Then** the DigiDollar balance updates, a "Received" entry appears in DigiDollar history, and the confirmation count is visible and climbs toward finality.
5. **Given** a user has generated one or more DigiDollar addresses previously, **When** they revisit the Receive page, **Then** all of their previously generated addresses are listed with per-address balance so they can pick one to reuse or generate a new one.

---

### User Story 2 - Send DigiDollar to another address (Priority: P2)

A user with a DigiDollar balance sends some of it to another DigiDollar address, sees the pending transaction in their history, and watches it confirm.

**Why this priority**: Once users can hold DigiDollar, letting them spend it is the next visible unlock. It closes the peer-to-peer loop and is the prerequisite for using the wallet to pay friends, family, or merchants that do not run DigiPay. It ships after P1 because a wallet that can only receive is still useful; a wallet that can only send has nothing to send.

**Independent Test**: With a wallet holding at least $5 of DigiDollar and enough DGB to cover the network fee, enter a valid recipient DigiDollar address, choose an amount, confirm, and verify that: the send appears as pending in history immediately, the recipient sees the funds, and the sender's balance updates once confirmed.

**Acceptance Scenarios**:

1. **Given** a user has a DigiDollar balance of at least the requested amount and enough DGB to pay the network fee, **When** they enter a valid recipient DigiDollar address, an amount, and confirm the send, **Then** the wallet submits the transaction, shows an immediate "Pending" entry in DigiDollar history, and updates the balance once the transaction confirms.
2. **Given** a user is entering a recipient, **When** the entered string is not a valid DigiDollar address for the current network (wrong prefix, bad checksum, DGB address by mistake, testnet address in a mainnet wallet, etc.), **Then** the send button is unavailable and an inline message explains why in plain language.
3. **Given** the user asks to send more than their available (confirmed) DigiDollar balance, **When** they attempt to continue, **Then** the wallet blocks the send and clearly states that the amount exceeds available balance.
4. **Given** the user has enough DigiDollar but not enough DGB to pay the network fee, **When** they attempt to send, **Then** the wallet blocks the send and prompts them to add DGB before trying again.
5. **Given** a user is about to sign a send, **When** the confirmation dialog opens, **Then** it shows the full recipient address (not truncated), the amount in US dollars, and the network fee, so nothing is hidden at the moment of consent.
6. **Given** a user has already submitted five DigiDollar sends in the last minute from this session, **When** they attempt a sixth send inside that window, **Then** the wallet blocks the attempt and asks them to slow down.

---

### User Story 3 - See DigiDollar network health and USD context (Priority: P3)

A user understands, at a glance, whether DigiDollar is operating normally and what one DGB is worth in USD right now, so they can make informed decisions before receiving or sending.

**Why this priority**: This story adds trust and context on top of P1 and P2. It is standalone: even before Send ships, showing "network is healthy, one DGB is worth $X, updated 12 seconds ago" is valuable to anyone holding or receiving DigiDollar. It ships last because it is polish, not a blocker for either receiving or sending.

**Independent Test**: On a wallet that already supports P1, load a DigiDollar page while the oracle price is fresh — verify the price and freshness are shown. Then simulate a degraded state (stale oracle, node lagging the chain tip, or a network-level error mode) and verify an informational banner appears without blocking the rest of the wallet.

**Acceptance Scenarios**:

1. **Given** a user has the DigiDollar feature enabled, **When** they open any DigiDollar page, **Then** the current oracle-provided USD value of one DGB is shown with a visible "last updated" freshness indicator.
2. **Given** the oracle price becomes stale (older than the acceptable freshness threshold) or the DigiDollar network reports a degraded state, **When** the user is on a DigiDollar page, **Then** a non-blocking banner explains the degraded state in plain language and stays visible until the situation recovers.
3. **Given** the wallet's underlying node has fallen significantly behind the network chain tip, **When** the user is looking at any DigiDollar balance or history view, **Then** a banner warns them that balances may be out of date.
4. **Given** the DigiDollar feature is enabled, **When** the user views their DGB balance on the home screen, **Then** an equivalent USD amount computed from the current oracle price is shown alongside it.

---

### Edge Cases

- The user enters a send amount below the DigiDollar minimum ("dust floor") — the send is blocked inline with a clear minimum.
- The user enters a send amount above the DigiDollar network maximum per transaction — the send is blocked inline with a clear maximum.
- The user enters a send amount that would leave DigiDollar change under $1.00 (for example, $9.50 from a $10.00 balance) — the send is blocked inline with an explanation and a "Send all" action (FR-012).
- The user sends DigiDollar to one of their own DigiDollar addresses — allowed, but the confirmation dialog notes "this is one of your own addresses" so it is not accidental.
- The wallet cannot reach the DigiDollar backend at all — DigiDollar sections show an unobtrusive "cannot reach network" state; the rest of the wallet keeps working from cached information. DigiDollar Send is disabled with a "cannot reach network" message (no offline queueing or deferred broadcast), and cached DigiDollar balance and history are shown labeled as cached.
- The user has the wallet open in two devices or tabs and sends from both — each send is processed independently and each view catches up on its next refresh; no cross-device lock is required.
- The DigiDollar feature is turned off remotely while the user is already on a DigiDollar page — the user is gently returned to the home screen and told the feature is temporarily unavailable.
- The user pastes a DigiByte (DGB) address into the DigiDollar send field by mistake — validation rejects it inline before the confirmation dialog can appear.
- The user copies a DigiDollar address for another network (for example, a testnet address into a mainnet wallet) — the send is blocked with a message that names the mismatch.
- The user restores their recovery phrase on a new device that has never seen DigiDollar — the wallet scans derived addresses (FR-007), and the DigiDollar balance and history reflect existing on-chain funds once the scan completes; until then a "searching for your DigiDollar" state is shown rather than $0.00.
- The active wallet was imported from a single private key, is watch-only, or is a multisig wallet — the DigiDollar section shows "DigiDollar requires a wallet created from a recovery phrase" instead of balances or actions; switching to a recovery-phrase wallet shows DigiDollar normally.

## Requirements *(mandatory)*

### Functional Requirements

**Feature enablement and visibility**

- **FR-001**: The wallet MUST show a DigiDollar section on the home screen when the DigiDollar feature is enabled for the user.
- **FR-002**: The wallet MUST hide every piece of DigiDollar UI (home section, navigation, dedicated pages, banners) when the feature is disabled, so the DGB experience is identical to today. The single exception is one neutral home-screen line, "DigiDollar is temporarily unavailable", shown only on devices that have previously used DigiDollar.
- **FR-003**: When DigiDollar is not yet activated on the connected network, the wallet MUST show "DigiDollar is not active on this network yet" with no receive or send actions, and MUST recheck on each refresh. When the DigiDollar backend is unreachable or reports itself unavailable, the wallet MUST degrade DigiDollar sections into a clearly-labeled "temporarily unavailable" state without breaking DGB features.

**Balances, addresses, and history**

- **FR-004**: The wallet MUST display DigiDollar balances in US dollars with two-decimal precision, shown separately as confirmed and pending.
- **FR-005**: The wallet MUST let a user generate a new DigiDollar receive address on demand.
- **FR-006**: The wallet MUST validate any address entered or shown as a receive address against the correct DigiDollar address format for the current network before displaying, saving, or sending to it.
- **FR-007**: The wallet MUST remember DigiDollar addresses the user has generated so they can revisit them and see the per-address balance. When a wallet is restored from a recovery phrase, or DigiDollar is opened for the first time on a device with no saved address list, the wallet MUST automatically scan the user's derived DigiDollar addresses in order, stop after 20 consecutive addresses with no on-chain activity, and rebuild the address list, balance, and history from the addresses found. The scan MUST show progress, MUST NOT block DGB features, and MUST be repeatable via the manual refresh control.
- **FR-008**: The wallet MUST NOT display DigiDollar private keys, seed material, or any equivalent recovery secret in the UI. DigiDollar address lists, cached history, and pending-send records kept on the device MUST be stored encrypted, under a key derived from the unlocked seed so they are readable only after the wallet PIN unlock, and MUST NOT contain private keys or seed material.
- **FR-009**: The wallet MUST show DigiDollar transaction history with, for each entry: direction (send or receive), counterparty address, amount in US dollars, confirmation count, timestamp, and a link to view the transaction on a public block explorer.
- **FR-010**: The wallet MUST refresh DigiDollar balance and history automatically at least every thirty seconds while a DigiDollar page is in the foreground, and MUST offer a manual refresh control.

**Sending DigiDollar** (User Story 2)

- **FR-011**: The wallet MUST allow a user to send DigiDollar to any valid same-network DigiDollar address they type or paste, up to the available confirmed balance.
- **FR-012**: The wallet MUST block sends whose amount is below $1.00 (the DigiDollar per-transaction minimum) or above $100,000.00 (the DigiDollar per-transaction maximum), showing the applicable limit inline. These limits are treated as protocol constants and are hardcoded in the wallet; if the protocol revises them, they are updated in a patch release. The $1.00 minimum also applies to the change output: the wallet MUST block a send whose remaining DigiDollar change would be greater than $0.00 and below $1.00, explain inline (send the full balance or leave at least $1.00), and offer a "Send all" action that sends the entire spendable DigiDollar balance with no change output.
- **FR-013**: The wallet MUST block sends when the user does not have enough DGB to cover the required network fee, and MUST tell the user how to fix it.
- **FR-014**: The wallet MUST display the DGB network fee cost to the user before they sign a send. The fee is calculated automatically by the wallet and is never below the protocol minimum; the Send screen MUST NOT offer fee selection or custom fee entry.
- **FR-015**: The wallet MUST require an explicit confirmation step for every send in which the full recipient address, USD amount, and DGB fee are visible together.
- **FR-016**: The wallet MUST limit sends to no more than five per minute per active session.
- **FR-017**: The wallet MUST record a send as "Pending" in local history the moment it is submitted, and update its status as confirmations arrive. Inputs spent by a pending send MUST be excluded from the available balance. A pending send remains "Pending" while the node reports it in the mempool or in a block; if it is absent from both for 30 consecutive minutes, the wallet MUST mark it "Failed", release the reserved funds, and notify the user.

**Network context and disclosures** (User Story 3)

- **FR-018**: The wallet MUST display the current oracle-provided USD value of DGB alongside a freshness indicator on every DigiDollar page.
- **FR-019**: The wallet MUST display a non-blocking banner when the oracle price is stale beyond an acceptable freshness threshold or when the DigiDollar network reports a degraded operating mode. When the oracle reports no usable price at all, the wallet MUST hide the DGB USD equivalent (FR-021), show "Price unavailable" in place of the price on DigiDollar pages with a non-blocking banner, and keep DigiDollar balance, receive, and send working.
- **FR-020**: The wallet MUST display a non-blocking banner when the wallet's underlying node is significantly behind the network chain tip.
- **FR-021**: When the DigiDollar feature is enabled, the wallet MUST display the USD equivalent of the user's DGB balance on the home screen, computed from the current oracle price.

**Rollout, safety, and privacy**

- **FR-022**: The wallet MUST read the DigiDollar-view and DigiDollar-send feature switches independently at runtime, so view can be enabled without send.
- **FR-023**: The wallet MUST NOT log DigiDollar addresses, transaction IDs, or oracle prices in a way that could be tied to an individual user across sessions; aggregate, non-identifying counters (for example, "send attempted") are acceptable.
- **FR-024**: All DigiDollar screens MUST be reachable and operable using the wallet's existing accessibility conventions (keyboard navigation, screen-reader labels, and a color-contrast baseline no worse than the rest of the wallet).
- **FR-025**: All DigiDollar user-facing text MUST be provided through the wallet's existing localization pipeline; DigiDollar amounts MUST respect the user's locale for number formatting.

**Send safety — items awaiting product decision**

- **FR-026**: The wallet MUST sign DigiDollar sends on the user's own device using DigiDollar keys derived from the user's existing wallet seed. The wallet operator MUST NOT hold, escrow, or have any ability to sign for the user's DigiDollar keys. The Send screen MUST make this custody model clear to the user in plain language.
- **FR-027**: When the DigiDollar network is in a degraded operating mode (stale oracle price beyond the acceptable freshness threshold, no usable oracle price at all, or a network-reported ERR state), the wallet MUST still allow outbound sends and MUST add an explicit extra warning line to the send confirmation dialog that names the specific degraded condition, so the user gives informed consent before signing.
- **FR-028**: The scope of this specification is explicit: creating new DigiDollar by locking DGB collateral (mint) and unlocking DGB by returning DigiDollar (redeem) are OUT of scope and will be addressed in a separate follow-up specification. The wallet MUST NOT expose a working mint or redeem flow in this release. A disabled "Mint - coming soon" teaser button on the DigiDollar page is allowed (it opens nothing and only shows a notice).
- **FR-029**: The DigiDollar experience MUST be available in the wallet on mainnet and on testnet26 in the first release. Regtest support is OUT of scope for v1 and will follow in a subsequent patch; on regtest wallets, the DigiDollar section MUST NOT appear (as if the DigiDollar feature switch were off).
- **FR-030**: DigiDollar MUST be available only for wallets created from or restored with a recovery phrase. For wallets imported from a single private key, watch-only wallets, and multisig wallets, the wallet MUST show a short explanation that DigiDollar requires a recovery-phrase wallet, and MUST NOT show DigiDollar balances, receive addresses, or send actions.

### Key Entities

- **DigiDollar balance**: The total US-dollar amount of DigiDollar a user holds across all of their DigiDollar addresses. Shown as confirmed and pending separately. Derived from the network; the wallet does not invent it.
- **DigiDollar address**: A network-specific string the user can share to receive DigiDollar. The wallet generates them on demand and remembers the ones the user owns.
- **DigiDollar transaction**: A record of DigiDollar moving into or out of an address the user owns. Has direction, counterparty, USD amount, confirmation count, timestamp, and a public transaction identifier.
- **Oracle price**: The current network-attested US-dollar price of DGB. Drives DGB-to-USD display and appears in banners when it is stale.
- **Network health status**: A summary of whether DigiDollar is operating normally or in a degraded state. Surfaces to the user only as banners; users do not act on it directly.
- **DigiDollar feature switches**: Runtime flags that control whether the DigiDollar view and DigiDollar send experiences are visible to a given user. Not shown to end users.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from opening the wallet home screen to a scannable DigiDollar receive address in three taps or fewer, in under fifteen seconds.
- **SC-002**: When a DigiDollar payment is broadcast to an address the user owns, it appears in the wallet's DigiDollar history within one minute of the network first seeing it, for at least ninety-nine percent of received transactions.
- **SC-003**: A user can complete a DigiDollar send — from opening the Send page to seeing "Pending" in history — in under sixty seconds when they already know the recipient address.
- **SC-004**: Zero confirmed cases per month of a DigiDollar send that reached an invalid address the wallet let through client-side validation.
- **SC-005**: In the first thirty days after the DigiDollar view feature is enabled, DigiDollar section views equal at least thirty percent of wallet home-screen views, measured only with anonymous aggregate counts that carry no per-user or per-device identifier (consistent with FR-023).
- **SC-006**: Of DigiDollar sends that pass in-wallet validation, at least ninety-five percent confirm on-chain within one hour without the user needing to retry or contact support.
- **SC-007**: Support tickets that mention DigiDollar remain below one ticket per one thousand DigiDollar transactions during the first ninety days after each rollout phase.
- **SC-008**: The rest of the wallet (DGB balance, DGB send, DGB history) remains fully usable when the DigiDollar backend is unreachable — zero DGB-facing regressions attributable to DigiDollar during any degraded-mode incident.

## Assumptions

- The wallet's underlying DigiByte node infrastructure supports DigiDollar (correct node version, DigiDollar mode enabled, a DigiDollar-capable wallet loaded). Standing up that infrastructure is out of scope for this spec.
- The wallet backend exposes the balance, address-generation, transaction history, send, oracle-price, and network-health surfaces required by the requirements above. Their exact shape is a design decision for the plan phase.
- Users of the DGB Wallet are already familiar with DGB balance, receive, and send flows; DigiDollar mirrors those flows so no separate onboarding is assumed.
- One DigiDollar is a stable one US dollar. This spec treats "$1 = 1 DD" as a given for display and does not attempt to price DigiDollar itself.
- Fees for DigiDollar transactions are paid in DGB, not in DigiDollar. This is why FR-013 and the Send confirmation dialog surface the DGB fee.
- DigiPay merchant acceptance of DigiDollar is a separate feature and lives in its own specification; this spec covers the wallet only.
- The following are explicitly out of scope for the first release: cold-storage / multisig custody of DigiDollar; push notifications for DigiDollar events; cross-device synchronization of "my DigiDollar addresses"; fiat on-ramps or off-ramps; any cross-chain bridge for DigiDollar; regtest support for DigiDollar (mainnet and testnet26 only in v1); and any KYC or sanctions-screening obligation on wallet users.
- Feature-flag governance is available at runtime for the DigiDollar view surface and DigiDollar send surface independently. This lets us ship dark, beta, and GA without a code change.
- The wallet retains its current progressive-web-app / offline-tolerant behavior; DigiDollar sections degrade gracefully when the backend cannot be reached, matching how DGB sections behave today.
