# Feature Specification: Your Addresses on the Receive Page

**Feature Branch**: `feature/receive-address-list`

**Created**: 2026-11-14

**Status**: Draft

**Input**: User description: "Add a 'Your addresses' list to the Receive page so the user can see all the receiving addresses they have generated, switch which one is shown (QR code, copy, share), name an address, and still generate a new one. HD wallets only; single-key wallets keep the existing legacy/SegWit switch. Keep the existing limit message when too many unused addresses are out. Names are the same labels used by the Activity address picker, so a name set in either place shows in both. Each listed address can open the Activity page filtered to that address. The list should include addresses with no transactions yet, showing used or unused."

## Clarifications

### Session 2026-11-14

- Q: How should the list decide whether an address is "used" or "unused"? → A: From the transaction history the wallet already loads: an address with a transaction is used, one without is unused, and when the history is not available the status shows as unknown. No per-address explorer requests are added.
- Q: How many addresses should the "Your addresses" list show at first? → A: The newest 10, with a "Show more" control that adds 10 at a time until all generated addresses are shown.
- Q: Should the existing name box under the QR code stay now that addresses can be named in the list? → A: Yes. Both stay, they edit the same name, and a change in one shows in the other at once.
- Q: What should "View activity" do on addresses that have no transactions yet? → A: It is hidden on unused addresses and on addresses whose status is unknown, so it only appears where it leads to real transactions.
- Q: When the user leaves Receive and comes back, which address should be on show? → A: Always the newest generated address, as today. Switching to an older address lasts only for the current visit and nothing is saved.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See and switch between my receiving addresses (Priority: P1)

A user who has generated several receiving addresses opens Receive and sees a "Your addresses" list. Choosing an address in the list shows it in the QR code, with its copy and share actions, so they can hand out any of their addresses again, not only the newest one.

**Why this priority**: This is the core ask. Today only the newest address can be shown, and older ones can be found only through the Activity list.

**Independent Test**: Generate three addresses, open Receive, pick the first one in the list, and confirm the QR code, copy and share now carry that address.

**Acceptance Scenarios**:

1. **Given** a wallet with several generated addresses, **When** the user opens Receive, **Then** a list shows the newest 10 addresses, newest first, with the address on show marked.
2. **Given** more than 10 generated addresses, **When** the user chooses "Show more", **Then** 10 more are added, until all are shown and the control disappears.
3. **Given** the list, **When** the user chooses another address, **Then** the QR code, the displayed address, and the copy, share and download actions use that address.
4. **Given** a payment request amount is entered, **When** the user switches address, **Then** the request (QR code and link) uses the newly chosen address and keeps the amount.
5. **Given** a wallet with only one generated address, **When** the user opens Receive, **Then** the list shows that one address and nothing looks broken.
6. **Given** the user switched to an older address, **When** they leave Receive and come back, **Then** the newest generated address is on show again.

---

### User Story 2 - Know which addresses are unused or used (Priority: P1)

Each listed address shows whether it has been used yet (it has received or sent funds) or is still unused, so the user can tell which addresses are safe to hand out fresh and which already have activity.

**Why this priority**: Includes addresses with no activity on purpose; those are the ones people hand out. Without the status the list is just a long list.

**Independent Test**: With one address that has been paid and one that has not, confirm each shows the right status.

**Acceptance Scenarios**:

1. **Given** an address that has a transaction, **When** the list is shown, **Then** it shows as used, with how many transactions it has.
2. **Given** an address with no transactions, **When** the list is shown, **Then** it shows as unused.
3. **Given** the status cannot be determined because the network is unreachable, **When** the list is shown, **Then** the addresses are still listed and the status is simply left out or marked as unknown, never wrongly shown as unused.

---

### User Story 3 - Name an address (Priority: P2)

The user can give an address a name (for example "Shop" or "Donations") from the list, change it, or remove it. Names are the same ones used on the Activity page, so a name set in either place shows in both.

**Why this priority**: Makes the list readable with many addresses and ties it to Activity; the existing single-address name box on Receive already exists.

**Independent Test**: Name an address in the Receive list, then open Activity and see the same name in its address picker and on its rows. Rename it on Activity and see it on Receive.

**Acceptance Scenarios**:

1. **Given** an unnamed address, **When** the user names it, **Then** the name shows in the list and in the name box under the QR code.
2. **Given** the address on show, **When** the user names it with the existing box under the QR code, **Then** the list shows that name at once.
3. **Given** a named address, **When** the user changes or clears the name, **Then** the list, the QR area and Activity all update.
4. **Given** the user starts naming an address, **When** they cancel, **Then** nothing changes.

---

### User Story 4 - Generate a new address (Priority: P2)

The user can still generate a new address. It appears at the top of the list, selected and ready to share. The existing limit still applies: when too many unused addresses are already out, the user sees the same explanatory message and no new address is made.

**Why this priority**: Existing behavior that must keep working and fit the list.

**Independent Test**: Generate a new address; it appears first and is on show. With the limit reached, generating shows the message and the list is unchanged.

**Acceptance Scenarios**:

1. **Given** the list, **When** the user generates a new address, **Then** it is added as the newest entry and shown in the QR code.
2. **Given** the limit of unused addresses in a row is reached, **When** the user tries to generate another, **Then** the existing message is shown and no address is added.
3. **Given** the new address could not be saved, **When** generating fails, **Then** the existing message is shown and the current address stays on show.

---

### User Story 5 - Open Activity for an address (Priority: P3)

Each listed address has a "View activity" action that opens the Activity page already filtered to that address.

**Why this priority**: Joins the two screens; useful but not needed for the list to work.

**Independent Test**: Choose "View activity" on a used address and see Activity filtered to it. For an unused address the action is hidden.

**Acceptance Scenarios**:

1. **Given** a used address, **When** the user chooses "View activity", **Then** Activity opens filtered to that address.
2. **Given** an unused address or one whose status is unknown, **When** the list is shown, **Then** "View activity" is not offered.

---

### Edge Cases

- A single-key (WIF) wallet: no list is shown; the legacy/SegWit switch stays as it is today.
- A wallet with many generated addresses (up to the existing limit and beyond, if the user has used many): the list stays usable on a phone and loads without freezing the page.
- The same address shown in two forms (bech32 checksum variants): treated as one address.
- A watch-only or multisig wallet: follow what Receive offers for those wallets today; this feature adds nothing there unless they already generate receiving addresses.
- Switching wallets: the list always belongs to the wallet currently active.
- The address on show when the page opens: always the newest generated address, as today, even if the user had switched to an older one on an earlier visit.
- Locked wallet: no addresses are listed.
- A very long name: shortened in the list, full in the editor.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: For HD wallets, the Receive page MUST show a list of the receiving addresses the user has generated, newest first, with the address on show marked. It MUST show the newest 10 at first and offer "Show more", which adds 10 at a time until every generated address can be reached.
- **FR-002**: Choosing an address in the list MUST make the QR code, displayed address, and copy, share and download actions use it, and MUST keep any entered payment request amount and note.
- **FR-003**: Each listed address MUST show whether it is unused or used (with its transaction count when known); when the status is unknown it MUST NOT be shown as unused.
- **FR-004**: The list MUST include addresses that have no transactions yet.
- **FR-005**: Users MUST be able to set, change and remove an address's name from the list, using the same names as the Activity page; a change MUST show at once in both places. The existing name box under the QR code MUST stay and edit the same name, so a change made there shows in the list and the other way round.
- **FR-006**: Users MUST still be able to generate a new address; it MUST appear as the newest entry and be shown.
- **FR-007**: The existing limit on unused addresses in a row, and its user-facing message, MUST keep working unchanged, as MUST the existing message when a new address cannot be saved.
- **FR-008**: Each used address MUST offer "View activity", opening Activity filtered to that address. The action MUST be hidden on unused addresses and on addresses whose status is unknown.
- **FR-009**: Single-key wallets MUST keep today's legacy/SegWit switch and MUST NOT show the list.
- **FR-010**: The list MUST be shown from the wallet's own derivation and the transaction history the wallet already loads; the feature MUST NOT send addresses or names to any new service.
- **FR-011**: Showing the list MUST NOT change which address the wallet treats as its current receiving address, except when the user generates a new one.
- **FR-012**: Each time Receive opens, the newest generated address MUST be the one on show; switching to another address MUST last only for the current visit and MUST NOT be saved.

### Key Entities

- **Receiving Address (listed)**: One of the wallet's generated receiving addresses; has its text, its position in generation order, an optional name, and a status (unused, used, or unknown) with a transaction count when known.
- **Address Name**: The user's label for an address, shared with the Activity page.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can show any of their generated addresses in the QR code within 2 interactions from opening Receive.
- **SC-002**: 100% of generated addresses can be reached in the list (the newest 10 at first, the rest through "Show more"), and used and unused addresses are told apart correctly in 100% of cases where the transaction history is available.
- **SC-003**: A name set on Receive appears on Activity, and the other way round, with no extra step.
- **SC-004**: With 50 generated addresses the Receive page is ready to use in under 3 seconds on a typical phone.
- **SC-005**: Generating a new address and the existing limit message behave exactly as before in all cases covered by the existing tests.

## Assumptions

- Only the SegWit receiving addresses of an HD wallet are listed (what Receive shows today); legacy forms are out of scope.
- "Generated" means every address up to and including the newest one the wallet has handed out.
- Usage status comes only from the transaction history the wallet already loads (clarified); no per-address lookup is added. Addresses found in that history are used; the others are unused when the history is loaded and unknown when it is not.
- Multi-select, deleting or hiding addresses, and exporting the list are out of scope.
- Change addresses are not shown.
- The Activity address filter from spec 010 already exists and is reused for "View activity".
