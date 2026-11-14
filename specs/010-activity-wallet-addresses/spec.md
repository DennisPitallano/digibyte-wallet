# Feature Specification: Wallet Addresses in Activity

**Feature Branch**: `feature/activity-wallet-addresses`

**Created**: 2026-11-14

**Status**: Draft

**Input**: User description: "Is it possible to display the wallet address per transaction on the activity list? Also display all the used addresses, also use it as filter? The case is if the user generates new addresses for receiving."

## Clarifications

### Session 2026-11-14

- Q: For a sent transaction, which of your addresses should the activity row show? → A: The first funding address plus "+N more" when there are several; the details view lists all of them.
- Q: Where should the list of used addresses live? → A: An address picker in the activity filter bar; each entry shows label, address and transaction count, and choosing one applies the filter.
- Q: Should the used-address picker also list receiving addresses with no transactions yet? → A: No; only addresses with at least one transaction (pending included).
- Q: How should a transfer between two of your own addresses appear and filter? → A: One row with its current direction, showing the funding address; it matches the address filter for either the funding or the receiving address, and the details view shows both.
- Added after the first build (requested by the user): the chosen address is remembered between visits (User Story 4), and addresses can be named from the picker (User Story 5). This replaces the earlier default that the filter resets when the page is reopened.
- Q: When one received transaction pays more than one of your addresses, what should the row show? → A: The first paid address plus "+N more"; the details view lists all of them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See which of my addresses a transaction used (Priority: P1)

A user who has generated several receiving addresses opens the activity list and wants to know, for each transaction, which of their own wallet addresses it paid into (for received funds) or was spent from (for sent funds), so they can match payments to the address they gave out.

**Why this priority**: This is the core ask and the minimum useful slice; without it, new receiving addresses are indistinguishable in the history.

**Independent Test**: Generate two receiving addresses, receive a payment to each, open the activity list, and confirm each transaction shows the correct one of the user's addresses.

**Acceptance Scenarios**:

1. **Given** a wallet with a received transaction paid to one of its addresses, **When** the user views the activity list, **Then** that row shows the wallet address that received the funds.
2. **Given** a sent transaction funded from one or more wallet addresses, **When** the user views the list, **Then** the row shows the first funding address, with "+N more" when there are several, and the details view lists all of them.
3. **Given** a user-assigned label exists for an address, **When** the row is shown, **Then** the label is shown with (or in place of) the shortened address.
4. **Given** the user opens a transaction's details, **When** the details appear, **Then** the full wallet address is visible and copyable.

---

### User Story 2 - Browse all used addresses (Priority: P2)

From an address picker in the activity filter bar, the user can see every wallet address that has appeared in their activity, with how many transactions each has and its label if any.

**Why this priority**: Gives the overview needed to choose a filter and to audit address usage, but the per-transaction display already delivers value alone.

**Independent Test**: After receiving to three different addresses, open the address list and confirm all three appear with correct transaction counts.

**Acceptance Scenarios**:

1. **Given** transactions across several addresses, **When** the user opens the used-addresses view, **Then** each address appears once with its transaction count and label.
2. **Given** an address that was generated but never used, **When** the user opens the view, **Then** it is not listed as used.

---

### User Story 3 - Filter activity by address (Priority: P2)

The user picks one of their addresses and the activity list narrows to only transactions involving it, combinable with the existing All/Received/Sent filters and search.

**Why this priority**: Directly answers "use it as filter"; depends on Stories 1–2 for the address data.

**Independent Test**: With transactions on two addresses, choose one address and verify only its transactions remain; clear it and verify all return.

**Acceptance Scenarios**:

1. **Given** transactions across several addresses, **When** the user selects an address filter, **Then** only transactions involving that address are shown.
2. **Given** an address filter and "Received" are both active, **When** the list updates, **Then** only received transactions on that address are shown.
3. **Given** a filter matching nothing, **When** the list updates, **Then** a clear empty-state message is shown and the filter can be cleared in one action.

---

### User Story 4 - Remember the chosen address (Priority: P3)

When the user leaves the Activity page and comes back, the address they were filtering by is still selected, so they do not have to pick it again.

**Why this priority**: Convenience for people who track one address (for example a shop or donation address); the filter already works without it.

**Independent Test**: Choose an address, go to another page and return to Activity; the same address is still selected and the list is filtered.

**Acceptance Scenarios**:

1. **Given** an address is chosen, **When** the user leaves and returns to Activity, **Then** the same address is still chosen.
2. **Given** a remembered address, **When** the user clears the filter, **Then** nothing is remembered on the next visit.
3. **Given** a remembered address that no longer appears in the activity, **When** the page opens, **Then** it shows the full list with no filter.
4. **Given** several wallets, **When** the user switches wallet, **Then** each wallet keeps its own remembered address.

---

### User Story 5 - Name an address from the picker (Priority: P3)

From the address picker the user can give an address a name, change it, or remove it, without leaving the Activity page.

**Why this priority**: Makes the picker easier to read when several receiving addresses exist; labels can already be set on the Receive page.

**Independent Test**: Open the picker, name an address, and see the name on the picker entry, on the rows that used it, and in search.

**Acceptance Scenarios**:

1. **Given** an address without a name, **When** the user names it in the picker, **Then** the name shows in the picker, on its rows and is found by search.
2. **Given** a named address, **When** the user changes or clears the name, **Then** all those places update at once.
3. **Given** the user starts naming an address, **When** they cancel, **Then** nothing changes.

---

### Edge Cases

- A sent transaction spending from several wallet addresses at once: all are associated with it, and filtering by any one of them includes it.
- A received transaction paying several wallet addresses in one transaction: the row shows the first paid address plus "+N more", the details view lists all of them, and filtering by any of them includes the transaction.
- A transfer between two of the user's own addresses: shown as a single row (direction unchanged) with the funding address; the address filter matches it for either the funding or the receiving address, and the details view shows both.
- Change addresses: change outputs back to the wallet should not appear as "used receiving addresses" or confuse the received/sent view.
- Transactions not yet confirmed (pending) still show and filter by address.
- Address data unavailable for an older/pending transaction: row shows no address instead of an error or a wrong one.
- Wallets with many addresses (hundreds): list and filter stay usable.
- Both HD wallets and single-key wallets, and both address formats the wallet supports.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each activity row MUST show the user's own wallet address involved in that transaction, abbreviated for readability, distinct from the counterparty address. For a received transaction this is the first paid address; for a sent transaction it is the first funding address. In both cases "+N more" follows when several of the user's addresses were involved.
- **FR-002**: Where a user label exists for an address, the system MUST show the label alongside or in place of the abbreviated address.
- **FR-003**: Transaction details MUST show the full wallet address(es) involved, with a way to copy them.
- **FR-004**: The activity filter bar MUST provide an address picker listing all wallet addresses that appear in the user's activity, each with transaction count and label (only addresses with at least one transaction, pending included, are listed); choosing an entry applies the address filter (FR-005).
- **FR-005**: Users MUST be able to filter the activity list by a single wallet address, and clear that filter in one action.
- **FR-006**: The address filter MUST combine with the existing All/Received/Sent filter and the search box.
- **FR-007**: Change-only addresses MUST NOT be presented as used receiving addresses.
- **FR-008**: Transactions involving several wallet addresses MUST be associated with every one of them for display and filtering.
- **FR-009**: Search in the existing search box MUST also match the user's own wallet addresses and their labels.
- **FR-011**: The chosen address filter MUST be remembered per wallet and restored on the next visit to Activity when that address still has activity; clearing the filter MUST forget it.
- **FR-012**: Users MUST be able to set, change and remove an address's label from the address picker, using the same labels as the Receive page; the change MUST show at once in the picker, on rows and in search.
- **FR-010**: The feature MUST NOT expose address data to anyone other than the wallet's user, and MUST NOT send it to external services.

### Key Entities

- **Wallet Address (used)**: One of the user's own addresses seen in activity; has the address text, optional label, and a count of transactions.
- **Transaction**: An activity entry; gains an association to the wallet address(es) it received to or spent from, in addition to its counterparty.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a wallet with at least 3 receiving addresses, 100% of received transactions display the correct receiving address.
- **SC-002**: A user can narrow the activity list to one address in no more than 2 interactions.
- **SC-003**: The used-addresses view lists 100% of addresses that have at least one transaction and none that have none.
- **SC-004**: With 500 transactions and 100 addresses, showing the list and applying an address filter takes under 1 second.
- **SC-005**: Users can identify which address a given payment arrived on without leaving the activity screen.

## Assumptions

- "Wallet address" means the user's own address in the transaction, not the counterparty address (which is already shown).
- The filter selects one address at a time; multi-select is out of scope.
- Existing address labels (set on the Receive page) are reused; no new labeling feature is added.
- The address list defaults to most recently used first.
- Scope is the wallet's DGB activity list; DigiDollar and Pay merchant activity are out of scope.
- Where the wallet cannot determine the address for a transaction, it is simply omitted for that row.
