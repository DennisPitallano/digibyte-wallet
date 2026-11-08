# Feature Specification: Digi-ID Address Funds Safety

**Feature Branch**: `feature/digiid-funds-safety`

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "Digi-ID address funds safety: (1) Show a clear warning in the wallet UI wherever a Digi-ID address is displayed that the Digi-ID address is an identity address, is not monitored by the wallet, and must not be used to receive payments. (2) Detect and recover funds that were sent to Digi-ID addresses anyway: scan the known Digi-ID site addresses (from login history) for balances, show any found funds, and let the user sweep them to a regular receive address after unlocking, with fee estimation, confirmation, and clear status. Wallet must be HD with private keys (not watch-only)."

## Clarifications

### Session 2026-11-08

- Q: When sweeping several Digi-ID addresses at once, one combined transaction or one per address? → A: One transaction per Digi-ID address, all behind a single user confirmation (keeps sites unlinked on-chain).
- Q: Should each swept Digi-ID address's funds go to a different fresh receive address or the same one? → A: A different fresh, unused receive address for each swept Digi-ID address.
- Q: If the login history is empty (e.g. after a restore from seed), may the user enter a site domain to check its Digi-ID address? → A: Yes. The user can type a site domain, and it is checked alongside the history entries.

### Session 2026-11-08 (second pass)

- Q: Where does the user find the check-for-funds tool? → A: A "Recover funds" section on the Identity page, linked from every warning and from Help.
- Q: Should sweep status survive closing or reloading the wallet before confirmation? → A: Yes. The wallet remembers sent sweeps and shows their status (pending, confirmed, failed) in the Recover funds section until they confirm.

### Session 2026-11-08 (third pass)

- Q: Should the check look up each Digi-ID address separately or all together in one request? → A: Each address in its own separate request, so the balance service can't tell the addresses belong together.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Warn users not to pay to a Digi-ID address (Priority: P1)

A Digi-ID address is a real DigiByte address, so anyone who sees it (the user, or a site that received it) can send money to it. The wallet never monitors these addresses, so money sent there is invisible in the balance. Today nothing tells the user this. Wherever the wallet shows a Digi-ID address, it must say plainly that the address is for sign-in only and must not receive payments.

**Why this priority**: It prevents the problem. It is cheap, has no money-movement risk, and protects every user right away, so it is the MVP.

**Independent Test**: Open each screen that shows a Digi-ID address and check that the warning is visible next to the address, in every supported language, without any further action from the user.

**Acceptance Scenarios**:

1. **Given** a user views a Digi-ID address on the Identity page, **When** the page renders, **Then** a warning next to the address states that it is an identity address, is not tracked by the wallet, and must not be used to receive payments.
2. **Given** a user opens the login detail view for a past Digi-ID login, **When** the address is displayed, **Then** the same warning is shown.
3. **Given** a Digi-ID link opens the approval prompt, **When** the prompt shows the address that will identify the user, **Then** the warning is visible before the user approves.
4. **Given** a user reads the Help content about Digi-ID, **When** the address is explained, **Then** the content states that the address is not for receiving funds and points to the recovery option.
5. **Given** the wallet language is Spanish or English, **When** any warning is shown, **Then** it appears in that language.
6. **Given** a user tries to copy a Digi-ID address, **When** the copy action completes, **Then** the warning stays visible, so the address is not copied without it.

---

### User Story 2 - Find funds sent to Digi-ID addresses (Priority: P2)

A user, or someone else, sent money to a Digi-ID address by mistake, and the wallet balance does not show it. The user needs a way to check. The wallet checks the Digi-ID addresses it knows about from the login history and reports any funds it finds.

**Why this priority**: The user must be able to see the lost funds before they can recover them. It is useful on its own as a diagnostic, but less urgent than prevention.

**Independent Test**: Send a small amount to a known Digi-ID address on a test network, run the check, and confirm the funds and the site they belong to are listed. Run it with no funds sent and confirm a clear "nothing found" result.

**Acceptance Scenarios**:

1. **Given** an unlocked HD wallet with Digi-ID login history and funds on one of those addresses, **When** the user runs the check, **Then** each address holding funds is listed with its site name, address and amount, plus a total.
2. **Given** no Digi-ID address holds funds, **When** the user runs the check, **Then** the wallet states that nothing was found.
3. **Given** the wallet is locked, **When** the user opens the "Recover funds" section, **Then** the wallet asks them to unlock first and does not run the check or reveal any key.
4. **Given** the wallet is watch-only, **When** the user opens the "Recover funds" section, **Then** the wallet explains that recovery needs a wallet with private keys, and no check or sweep is offered.
5. **Given** the network is unreachable during the check, **When** the check fails, **Then** the user sees a clear error and can retry, and no result is shown as "nothing found".

---

### User Story 3 - Sweep recovered funds to a regular address (Priority: P3)

Once funds are found, the user wants them in the normal wallet balance. They review the amount, the network fee and the destination, confirm, and the wallet moves the funds to the user's own regular receive addresses.

**Why this priority**: This is the recovery itself, but it depends on User Story 2 and carries the highest risk because it moves money.

**Independent Test**: With funds found on a Digi-ID address, run the sweep and confirm the amount minus the fee arrives at a regular receive address, the Digi-ID address ends up empty, and the wallet balance rises accordingly.

**Acceptance Scenarios**:

1. **Given** funds were found, **When** the user starts a sweep, **Then** the wallet shows the amount, an estimated network fee, the amount that will arrive, and the destination address for each swept address (each a different one of the user's own regular receive addresses) before anything is sent.
2. **Given** the user reviewed the summary, **When** they confirm, **Then** the wallet broadcasts the transaction and shows its progress until it is confirmed or fails.
3. **Given** a sweep succeeded, **When** the transaction confirms, **Then** the swept funds appear in the normal balance and transaction history, and the Digi-ID address shows no funds.
4. **Given** the funds on an address are worth less than the fee needed to move them, **When** the wallet builds the summary, **Then** it tells the user the funds are too small to sweep and sends nothing.
5. **Given** funds sit on several Digi-ID addresses, **When** the user selects several of them and chooses to sweep them, **Then** one confirmation covers all addresses, each address is swept in its own separate transaction (so the sites are never linked on-chain), and a failure on one address does not stop the others.
6. **Given** the sweep fails or is rejected by the network, **When** the failure is reported, **Then** the funds remain on the Digi-ID address and the user can retry.
7. **Given** the user cancels at the confirmation step, **When** they dismiss it, **Then** nothing is sent.

---

### Edge Cases

- Funds were sent to a Digi-ID address for a site the user never logged in to on this device. The wallet cannot discover it from login history, but the user can enter the site's domain by hand. Help content explains this.
- The login history was cleared or the wallet was restored from seed, so the history is empty. The check of the history finds nothing, and the Recover funds section prompts the user to enter site domains by hand.
- The user enters a domain that is malformed, has a different spelling or case, or includes a path or port. The wallet normalises it the same way as a login domain, or rejects it with a clear message.
- The user logged in to the same site several times. The wallet checks that site's address once.
- Some separate lookups succeed and others fail. The wallet shows results for the addresses that succeeded, marks the failed ones as "could not check", and lets the user retry only those.
- Funds on a Digi-ID address are unconfirmed. They are shown as pending and are not swept until confirmed.
- A sweep is started twice, for example by a double click, or again after a reload while the first is still pending. Only one transaction is sent per address.
- The user closes or reloads the wallet while a sweep is pending. On return, the Recover funds section still shows that sweep's status, and the Digi-ID address is not offered for a second sweep while its funds are in flight.
- The wallet locks (auto-lock) while a sweep is waiting at the review step. Nothing is sent and nothing is recorded, and the user is asked to unlock and review again.
- The wallet locks (auto-lock) while a sweep is running. No further address is swept after the lock: any address not yet sent is marked failed with the reason "wallet locked" and can be swept again after unlocking. Sweeps already broadcast are unaffected, and their status can still be refreshed after unlocking.
- A destination address turns out to be already used. The wallet picks the next unused regular receive address instead, and never reuses one destination for two swept addresses.
- A DigiDollar or other token output is on a Digi-ID address. Only plain DigiByte funds are handled: an output that is not an ordinary payment to the address is left untouched, and the user is told how many such outputs were left (FR-016).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The wallet MUST show a warning next to every place a Digi-ID address is shown in full or can be copied: the Identity page (approval prompt and success result), the login detail view, and the Help content. A shortened address in the Login History list row is exempt, because tapping the row opens the login detail view, which carries the warning.
- **FR-001a**: The recovery tool (check and sweep) MUST live in a "Recover funds" section on the Identity page. Every Digi-ID address warning, and the Help content, MUST link to it.
- **FR-002**: The warning MUST state three things: the address is an identity address, the wallet does not monitor it, and it must not be used to receive payments.
- **FR-003**: The warning text MUST be available in every language the wallet supports.
- **FR-004**: The wallet MUST let an unlocked wallet with private keys check the Digi-ID addresses from its login history for funds.
- **FR-004a**: The wallet MUST let the user enter a site domain by hand to check that site's Digi-ID address, in addition to the history entries. The entered domain MUST be validated and normalised the same way as a domain from a Digi-ID login, so it yields the same address.
- **FR-004b**: The wallet MUST look up each Digi-ID address in its own separate request to the balance service, and MUST NOT put several Digi-ID addresses in one request, so the service cannot link the sites to each other. The same applies to looking up the status of sweep transactions.
- **FR-005**: The check MUST list, for each address holding funds, the site, the address, the confirmed amount and any pending amount, plus a total.
- **FR-006**: The wallet MUST NOT run the check or reveal any key while it is locked, and MUST NOT offer it for watch-only wallets.
- **FR-007**: The wallet MUST distinguish "nothing found" from "could not check", and MUST allow a retry after a failed check.
- **FR-008**: The wallet MUST let the user sweep funds from the found addresses to one of the user's own regular receive addresses.
- **FR-008a**: The wallet MUST NOT combine funds from different Digi-ID addresses in one transaction, so that sites cannot be linked to each other on-chain. Each Digi-ID address is swept in its own transaction, and each transaction MUST pay to a different fresh, unused regular receive address, so the sites also cannot be linked through a shared destination.
- **FR-009**: Before sending, the wallet MUST show the amount, the estimated fee (total and per address), the amount that will arrive and the destination, and MUST require explicit confirmation. A single confirmation covers all selected addresses.
- **FR-010**: The wallet MUST refuse to sweep an address whose funds do not cover the fee, and MUST explain why.
- **FR-011**: The wallet MUST show clear sweep status (sending, pending, confirmed, failed), keep the funds on the Digi-ID address if the sweep fails, and keep the shown status current while the user is looking at it: a refresh button, plus an automatic refresh about every 30 seconds while the Recover funds section is open and a sweep is pending. No refresh happens while the section is closed (FR-014).
- **FR-011a**: The wallet MUST remember sweeps that were sent and keep showing their status in the "Recover funds" section after the wallet is closed, reloaded or re-opened, until each sweep is confirmed or has failed. This record MUST NOT contain any private key.
- **FR-012**: The wallet MUST prevent a sweep from being sent twice for the same funds.
- **FR-013**: The wallet MUST show swept funds in the normal balance and transaction history once the sweep is confirmed.
- **FR-014**: The wallet MUST NOT start monitoring Digi-ID addresses in the background or add them to the normal receive flow. Checking happens only when the user asks.
- **FR-015**: The wallet MUST NOT store or display private keys for Digi-ID addresses at any point in the check or sweep.
- **FR-016**: The wallet MUST sweep only plain DigiByte outputs. Where the network lookup shows that an output is not an ordinary payment to the address itself (for example a token), the output MUST be left untouched, excluded from every amount and fee, and reported to the user as "other outputs left untouched". Where the lookup does not say, the output is treated as an ordinary payment.

### Key Entities *(include if feature involves data)*

- **Digi-ID Address**: A sign-in address tied to one site and derived from the wallet seed. Attributes: site, address, and login history entries.
- **Digi-ID Fund Finding**: The result of a check for one Digi-ID address. Attributes: site, address, confirmed amount, pending amount, whether it can be swept.
- **Sweep**: A user-confirmed move of funds from one or more Digi-ID addresses to a regular receive address. It is made of one transaction per source address. Attributes: source addresses, destination, total amount, fee, status per address (sending, pending, confirmed, failed). A sent sweep is remembered until it is confirmed or has failed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of screens that show a Digi-ID address in full, or let it be copied, also show the warning, in every supported language.
- **SC-002**: A user can find out whether funds sit on their Digi-ID addresses in under 30 seconds from opening the Recover funds section, for a wallet with up to 50 logged-in sites.
- **SC-003**: A user can sweep found funds to their regular balance in under 2 minutes, not counting network confirmation time.
- **SC-004**: In testing, 100% of swept funds, less the fee shown beforehand, arrive at the user's regular address, and the fee actually paid is within 10% of the estimate shown beforehand.
- **SC-005**: In testing, a locked wallet and a watch-only wallet reveal no keys and send no transactions in 100% of attempts.
- **SC-006**: Support reports of "missing funds after sending to a Digi-ID address" can be resolved by the user alone, with no manual key handling.

## Assumptions

- Digi-ID addresses to check come from the wallet's existing Digi-ID login history plus any site domains the user enters by hand. The wallet cannot guess sites that are in neither.
- Checking is manual and on demand, not automatic. Funds sent to Digi-ID addresses are rare, and background monitoring would add cost and privacy exposure.
- The destinations for a sweep are the next unused regular receive addresses of the same wallet, one per swept Digi-ID address. Letting the user pick an external address is out of scope.
- The fee uses the wallet's minimum fee rate, the floor the wallet already applies to every send. No new fee settings are added.
- Only plain DigiByte funds are swept (FR-016). DigiDollar and other token outputs are never moved by this feature. The wallet can only recognise them when the network lookup describes the output, so the Help text also tells users never to send tokens to a Digi-ID address.
- The wallet's existing balance lookup service is reused, so the user's privacy and network settings apply to this check too. Lookups are made one address at a time (FR-004b), which is expected to stay within SC-002 for up to 50 sites.
- Multi-wallet support follows the active wallet only. Other wallets are checked when the user switches to them.
- The warning wording will follow the wallet's existing tone and translation process. "Every supported language" means every translation file the wallet ships (currently English and Spanish). Other languages fall back to English, as the wallet already does for any missing text. The Help entry is English only, like the rest of Help.
- One confirmation covers at most 19 Digi-ID addresses: one fewer than the wallet's limit of 20 unused receive addresses in a row, so that after the sweep the Receive page can show a fresh unused address instead of one that now holds swept funds. In practice a wallet has room for 18, because the address the Receive page already shows is one of those 20. If room is smaller still, or more addresses have funds, the rest are swept in a further round once the first confirms, and the review says how many remain.
