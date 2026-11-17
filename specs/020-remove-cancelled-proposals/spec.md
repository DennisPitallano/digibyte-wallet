# Feature Specification: Remove Cancelled Proposals

**Feature Branch**: `feature/remove-cancelled-proposals`

**Created**: 2026-11-18

**Status**: In progress (feature built; tests, docs and browser check open, see tasks.md)

**Input**: User description: "Remove cancelled multisig proposals from the Pending Transactions list. Per-proposal 'Remove from list' action and a 'Clear all cancelled' action (shown when more than one cancelled). Local-device only: nothing on-chain or on co-signers' devices changes; only Cancelled status is removable (never open, failed or broadcast). No 'reopen' capability (create a new proposal instead). The 'Cancelled or expired' label covers Cancelled status only."

## Clarifications

### Session 2026-11-18

- Q: When someone removes a cancelled proposal from Pending, should it also disappear from the wallet's Activity history? → A: Yes, it is removed everywhere on this device (Pending and Activity).
- Q: Should "Clear all cancelled" ask the person to confirm before deleting, while single-item "Remove from list" stays instant? → A: Yes, confirm before "Clear all cancelled" only; single removal stays instant.

## Context

Cancelling a multisig proposal marks it as cancelled on this device only. It stays in the Pending Transactions list forever, dimmed at the bottom, and offers no further action. Users asked whether it can be deleted or reset. Over time these closed items clutter the list and bury payments that still need attention. This feature lets a person tidy them away. It does not add any way to reopen a cancelled proposal; the answer there is to create a new payment.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Remove one cancelled proposal (Priority: P1)

A person sees a card labelled "Cancelled or expired" in the Pending list. It has nothing left to do, so they remove it from the list with one action.

**Why this priority**: This is the core ask: get rid of dead items.

**Independent Test**: Cancel a proposal, open Pending, remove it, and confirm it no longer appears, including after a reload.

**Acceptance Scenarios**:

1. **Given** a cancelled proposal in the list, **When** the person chooses "Remove from list", **Then** it disappears and a short confirmation says it was removed from this device's list.
2. **Given** several cancelled proposals, **When** the person removes one, **Then** only that one disappears.
3. **Given** a removed proposal, **When** the person reloads or reopens the wallet, **Then** it stays gone.

---

### User Story 2 - Clear all cancelled proposals at once (Priority: P2)

A person with many cancelled items clears them all in one action instead of removing each.

**Why this priority**: Convenience on top of the core removal.

**Independent Test**: Have three cancelled and one open proposal; clear all cancelled; only the open one remains.

**Acceptance Scenarios**:

1. **Given** two or more cancelled proposals, **When** the person chooses "Clear all cancelled", **Then** all cancelled proposals disappear and the confirmation states how many were removed.
2. **Given** the person chooses "Clear all cancelled", **When** the confirmation appears, **Then** it states how many will be removed and that it can't be undone, and nothing is deleted until they confirm; choosing to keep them leaves the list unchanged.
3. **Given** only one cancelled proposal, **When** the list is shown, **Then** no "Clear all cancelled" action appears (the per-item action is enough).
4. **Given** open, ready, failed or broadcast proposals alongside cancelled ones, **When** the person clears all cancelled, **Then** those other proposals are untouched.

---

### Edge Cases

- A proposal that is awaiting signatures, ready to broadcast, failed or already broadcast never offers a remove action and can never be removed by this feature.
- Removing an item that was already removed (for example from a second tab) removes nothing and does not raise an error.
- A co-signer may still hold a copy of the removed proposal and could share it again; it then appears as a new proposal on this device, as any shared proposal would.
- Removal does not affect any other wallet's list.
- A removed proposal no longer appears in the wallet's Activity history either; any real on-chain transactions in Activity are unaffected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Pending list MUST offer a "Remove from list" action on each cancelled proposal.
- **FR-002**: When two or more cancelled proposals are listed, the list MUST offer a "Clear all cancelled" action.
- **FR-003**: "Clear all cancelled" MUST ask the person to confirm before deleting, stating how many items will be removed and that this cannot be undone; "Remove from list" on a single proposal MUST act immediately without a prompt.
- **FR-004**: Only proposals in the cancelled state MUST be removable; all other states MUST be left untouched by both actions.
- **FR-005**: Removal MUST be local to this device: no network transaction is created and co-signers' devices are not changed.
- **FR-006**: Removal MUST persist across reloads and sessions.
- **FR-007**: After removal, the person MUST see a notice that it applies to this device's list; the notice after "Clear all cancelled" also states how many items were removed.
- **FR-008**: The feature MUST NOT provide a way to reopen or reset a cancelled proposal.
- **FR-009**: A removed proposal MUST also disappear from the wallet's Activity history on this device; chain transactions in Activity MUST be unaffected.
- **FR-010**: Removing proposals MUST NOT change the cancelled label or behavior of proposals that remain.

### Key Entities

- **Pending proposal**: A payment awaiting signatures or already closed. A cancelled proposal is closed on this device and cannot be signed or broadcast from it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person can remove a cancelled proposal in one action (a single tap or click).
- **SC-002**: A person can clear any number of cancelled proposals in one action.
- **SC-003**: After clearing, 100% of non-cancelled proposals remain in the list unchanged.
- **SC-004**: A removed proposal does not reappear after a reload, in Pending or in Activity.

## Assumptions

- Single-item removal has no confirmation, because it is local, low-risk and the dead item has no remaining function. The bulk action asks first because it deletes several records at once.
- Removed proposals are deleted from this device entirely, so they leave both Pending and Activity.
- The "Cancelled or expired" label corresponds to the cancelled state only; nothing in the app currently expires proposals into a separate state.
- Reopening a cancelled proposal is out of scope; creating a new payment is the supported path.
