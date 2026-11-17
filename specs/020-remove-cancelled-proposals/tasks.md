# Tasks: Remove Cancelled Proposals

**Input**: Design documents from `/specs/020-remove-cancelled-proposals/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Required here. Constitution Principle III makes tests a merge gate for any change to `DigiByte.Wallet`, and `RemoveCancelledAsync` currently has none.

**Status**: Most implementation is already built and committed or in the working tree. Built tasks are ticked; open tasks are the tests, docs and browser check.

## Phase 1: Setup

None. No new projects, packages or configuration.

## Phase 2: Foundational

- [x] T001 Add `RemoveCancelledAsync(walletId, pendingId?)` to `src/DigiByte.Wallet/Services/MultisigWalletService.cs`. It deletes only proposals whose status is Cancelled (all, or the one with the given id), writes the list back to storage only if something was removed, and returns the count removed. Covers FR-004, FR-005, FR-006, FR-009.

**Checkpoint**: the service method exists; both user stories build on it.

## Phase 3: User Story 1 - Remove one cancelled proposal (Priority: P1) 🎯 MVP

**Goal**: A person removes a single cancelled proposal from the Pending list with one action.

**Independent Test**: Cancel a proposal, open Pending, remove it, confirm it is gone after reload and from Activity.

### Tests for User Story 1

- [x] T002 [US1] Create `tests/DigiByte.Wallet.Tests/MultisigRemoveCancelledTests.cs` using `MultisigFakeStorage` / `MultisigFakeChain` / `MultisigFakeCrypto` (pattern: `MultisigRenameTests.cs`). Add tests: removing by id deletes only that cancelled proposal and returns 1; the removal persists across a fresh service on the same storage; removing an id that no longer exists returns 0 and does not throw.
- [x] T003 [US1] In the same test file, add negative-path tests: a proposal in AwaitingSignatures, ReadyToFinalize, Failed or Broadcast status is never removed, even when its id is passed explicitly (returns 0, record intact); other wallets' pending lists are untouched.
- [x] T004 [US1] In the same test file, add a test that a removed cancelled proposal no longer appears in `GetActivityAsync` for that wallet (FR-009 / SC-004), and that chain transactions still do.

### Implementation for User Story 1

- [x] T005 [US1] Add a "Remove from list" button on each cancelled card in `src/DigiByte.Web/Pages/MultisigPending.razor`, acting immediately and showing a notice that it was removed from this device's list (FR-001, FR-004, FR-007).

**Checkpoint**: User Story 1 works and is test-covered.

## Phase 4: User Story 2 - Clear all cancelled proposals (Priority: P2)

**Goal**: A person clears all cancelled proposals at once, after confirming.

**Independent Test**: With three cancelled proposals and one open, clear all cancelled, confirm; only the open one remains.

### Tests for User Story 2

- [x] T006 [US2] In `tests/DigiByte.Wallet.Tests/MultisigRemoveCancelledTests.cs`, add tests: `RemoveCancelledAsync(walletId)` with no id removes every cancelled proposal and returns the count, and leaves open, failed and broadcast proposals unchanged (FR-004, SC-003).

### Implementation for User Story 2

- [x] T007 [US2] Add the "Clear all cancelled" action to `src/DigiByte.Web/Pages/MultisigPending.razor`, shown only when more than one cancelled proposal exists (FR-002).
- [x] T008 [US2] Add the inline confirmation: "Remove N cancelled proposals?" with "This can't be undone", a "Remove N" button and a "Keep them" button; nothing is deleted until confirmed (FR-003).

**Checkpoint**: both user stories work.

## Phase 5: Polish & Cross-Cutting

- [x] T009 Regenerate `src/DigiByte.Web/wwwroot/css/tailwind.css` via the build so the new classes are included.
- [x] T010 [P] Update `docs/PROCESS_FLOWS.md` (Cancel line, ~707) to say cancelled proposals can be removed from this device, local only, and that removal also clears them from Activity.
- [x] T011 Run `dotnet test tests/DigiByte.Wallet.Tests` and confirm all tests pass, including the new ones.
- [ ] T012 Walk through `specs/020-remove-cancelled-proposals/quickstart.md` in the browser and confirm each step, including that a cancelled proposal offers no reopen or reset action (FR-008) and that remaining proposals keep their labels (FR-010).
- [ ] T013 Commit the whole `specs/020-remove-cancelled-proposals/` folder and the `MultisigPending.razor` confirmation changes. Use a `feat:` / `test:` / `docs:` Conventional Commit message.

## Dependencies & Execution Order

- T001 comes before everything else.
- US1 and US2 are independent of each other once T001 exists.
- Tests T002-T004 and T006 share one file, so they are written together even though they cover different stories. Because they share a file, none of them is marked [P]; T010 (docs) is the only parallel task.
- T011-T012 come after T002-T008. T013 comes last.

## Implementation Strategy

MVP is User Story 1: the service method plus the per-item button, with its tests. User Story 2 is an increment on top. Because the code already exists, the remaining work is mostly T002-T004, T006, T010-T013.
