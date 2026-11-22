---

description: "Task list for the multisig wallet experience (list, navigation, pending, signing room)"
---

# Tasks: Multisig Wallet Experience (List, Navigation, Pending, Signing Room)

**Input**: Design documents from `specs/026-multisig-wallet-experience/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/ui-contracts.md](contracts/ui-contracts.md), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (Principle III) requires tests written first, and failing, for every change in `src/DigiByte.Wallet/`; the plan applies the same rule to `ShellContext` and `ScanRouter` in `src/DigiByte.Web/`. Screens have no component tests; they are checked with the quickstart walkthrough.

**Organization**: Tasks are grouped by user story so each story can be built, checked and shipped on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The user story the task belongs to (US1 to US5)
- All paths are relative to the repository root

## Path Conventions

- Decision logic: `src/DigiByte.Wallet/`
- Screens, menu and shell services: `src/DigiByte.Web/`
- Tests for both: `tests/DigiByte.Wallet.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Start from a clean, known state

- [X] T001 Create the feature branch `026-multisig-wallet-experience` from `main`, then run `dotnet build DigiByte.slnx` and `dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~Multisig|FullyQualifiedName~ScanRouter"` and record that both pass before any change
- [X] T002 Write the existing-actions checklist (sign, share by QR/copy/file, import signature by paste/scan/file, open/join/close/leave room, decline, broadcast, retry, cancel, restore cancelled, remove cancelled, clear all cancelled, rename, claim unassigned, drafts) with the screen each lives on today, in specs/026-multisig-wallet-experience/checklists/existing-actions.md (used for SC-012)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The counts and shell services every story reads

**⚠️ CRITICAL**: No user story work starts until this phase is complete

### Tests first (must fail before T007 to T010)

- [X] T003 [P] Add failing tests for `CountBadge.Text(n)`: "" for 0 and negatives, the number for 1 to 99, "99+" above 99, in tests/DigiByte.Wallet.Tests/MultisigUxCountBadgeTests.cs
- [X] T004 [P] Extend tests/DigiByte.Wallet.Tests/MultisigUxAttentionTests.cs with failing cases for the new per-wallet and total figures `NeedsSignature`, `ReadyToBroadcast`, `FailedBroadcast`, `NeedsYou` (= sum of the three): needs-signature still requires a local key and not yet signed; `ReadyToFinalize` and `Failed` count with or without a local key; `Broadcast` and `Cancelled` never count; existing `Total` and `For` keep today's needs-signature meaning
- [X] T005 [P] Add failing tests that `MultisigWalletService.PendingChanged` fires once with the shared wallet id when a pending item is saved (create, sign, import, cancel, mark broadcast), when `RemoveCancelledAsync` removes at least one item, and when `DeleteWalletAsync` removes a wallet's pending items, and does not fire when nothing changed, in tests/DigiByte.Wallet.Tests/MultisigPendingChangedTests.cs (use the doubles in tests/DigiByte.Wallet.Tests/MultisigTestDoubles.cs)

### Implementation

- [X] T006 Add the presentation types from data-model.md to src/DigiByte.Wallet/Models/MultisigUxModels.cs: `PendingCardAction` enum (`ReviewAndSign`, `OpenRoom`, `GetSignatures`, `ReviewAndBroadcast`, `ReviewAndRetry`, `ReviewAndCancel`, `View`, `None`), `PendingGroups` record (`NeedsYou`, `WaitingForOthers`, `Cancelled` lists), `WalletCardStatusKind` enum (`NeedsSignature`, `ReadyToBroadcast`, `LiveRoom`, `WaitingForOthers`, `None`) with a `WalletCardStatusLine(Kind, Count)` record, `WalletCardModel` record, `RoomStageKind` enum (`Invite`, `Sign`, `Broadcast`, `Done`), `RoomCountdownModel(Text, IsUrgent)` record
- [X] T007 [P] Implement `CountBadge` in src/DigiByte.Wallet/Services/CountBadge.cs so T003 passes
- [X] T008 Extend `AttentionSummary` and `AttentionCounter.Count` in src/DigiByte.Wallet/Services/AttentionCounter.cs with the four figures so T004 passes, leaving `Total`, `For` and `All` unchanged
- [X] T009 Add `public event Action<string>? PendingChanged` to src/DigiByte.Wallet/Services/MultisigWalletService.cs and raise it from `SavePendingTransactionAsync`, `RemoveCancelledAsync` and `DeleteWalletAsync` so T005 passes
- [X] T010 [P] Create the scoped `ShellChrome` service (`bool HideNav`, `void SetHideNav(bool)`, `event Action Changed`, no event when the value is unchanged) in src/DigiByte.Web/Services/ShellChrome.cs
- [X] T011 Create the scoped `MultisigAttentionState` service in src/DigiByte.Web/Services/MultisigAttentionState.cs: `AttentionSummary Current`, `Task RefreshAsync()`, `event Action Changed`; it loads the active wallet's shared wallets with `ListWalletsAsync`/`GetConfigAsync`/`GetPendingTransactionsAsync`, uses `AttentionCounter`, refreshes on `MultisigWalletService.PendingChanged`, and on `AppState.OnChange` only when the active wallet id or unlock state differs from the values it remembered at its last refresh (that event carries no detail, so keep and compare them), and swallows a read failure by keeping an empty summary (edge case: show nothing, never a wrong number)
- [X] T012 Register `ShellChrome` and `MultisigAttentionState` as scoped services in src/DigiByte.Web/Program.cs

**Checkpoint**: `dotnet test tests/DigiByte.Wallet.Tests` passes; counts are available to every story

---

## Phase 3: User Story 1 - Always know where I am, and never get thrown out (Priority: P1) 🎯 MVP

**Goal**: Inside a shared wallet the menu reads Overview, Activity, Scan, Pending, More and acts on that shared wallet; every screen shows which shared wallet and personal wallet it belongs to; leaving is one obvious tap.

**Independent Test**: Quickstart section 1. Open a shared wallet, tap each menu position, confirm every destination stays inside it, the context line is on every screen, and leaving and returning each take one or two taps.

### Tests for User Story 1 (write first, must fail)

- [X] T013 [P] [US1] Add failing table-driven tests for `ShellContext.Resolve(path)` in tests/DigiByte.Wallet.Tests/ShellContextTests.cs: `multisig/{id}` → Shared/Overview; `/activity` → Activity; `/pending` and `/pending/{pendingId}` → Pending; `/send` → Shared with section None; reserved first segments `create`, `import`, `room`, `sign-room` → Personal; `multisig`, `help/multisig`, empty path, `transactions`, `payments` → Personal; leading/trailing slashes, upper case and a query string are tolerated
- [X] T014 [P] [US1] Extend tests/DigiByte.Wallet.Tests/ScanRouterTests.cs with failing cases for the new optional `sharedWalletId`: a bare address and a payment request with an amount return `/multisig/{id}/send?address=…&amount=…` (id escaped, amount in invariant culture); Digi-ID, proposal, redeem script, room invite and unknown codes return exactly what they return without the id; a null or empty id keeps today's `/send?…`

### Implementation for User Story 1

- [X] T015 [US1] Implement `ShellContext` (`Kind`, `SharedWalletId`, `Section`, static `Resolve`) in src/DigiByte.Web/Layout/ShellContext.cs so T013 passes
- [X] T016 [US1] Add the optional `sharedWalletId` parameter to `ScanRouter.Route` in src/DigiByte.Web/Services/ScanRouter.cs so T014 passes
- [X] T017 [P] [US1] Add `overview` and `pending` icons (outline and solid) to src/DigiByte.Web/Layout/NavIcons.cs
- [X] T018 [P] [US1] Add English and Spanish text for `nav.overview`, `nav.pending`, `nav.pending.aria`, `nav.shared.label` ("{0} shared wallet navigation"), `nav.shared.switch`, `nav.count.pending` and `scan.leaving_shared` to src/DigiByte.Web/wwwroot/locales/en.json and src/DigiByte.Web/wwwroot/locales/es.json
- [X] T019 [P] [US1] Create `SharedWalletSwitcher` in src/DigiByte.Web/Components/Multisig/SharedWalletSwitcher.razor per contracts/ui-contracts.md: sheet with "Back to *wallet*" first, the active wallet's other shared wallets with scheme and `NeedsYou` count from `MultisigAttentionState`, then "All shared wallets"; focus trapped, Esc and backdrop close, full names shown untruncated
- [X] T020 [US1] Create `SharedWalletContextLine` in src/DigiByte.Web/Components/Multisig/SharedWalletContextLine.razor: one button showing the shared wallet's name, scheme chip and "in *wallet*" with the wallet's colour dot, each name keeping at least 12 characters before truncating, opening `SharedWalletSwitcher` (depends on T019)
- [X] T021 [US1] Add the shared-mode styles to src/DigiByte.Web/wwwroot/css/app.css: the name tab attached to the top edge of `.nav-float-live` (and above the rail in the wide-screen media query), the count bubble on a tab (reused later by More), 44 px touch height, and no transition under `prefers-reduced-motion`
- [X] T022 [US1] Rework src/DigiByte.Web/Layout/NavMenu.razor to render from `ShellContext.Resolve(CurrentPath)`: in shared mode slot 1 is Overview → `/multisig/{id}`, slot 2 Activity → `/multisig/{id}/activity`, slot 4 Pending → `/multisig/{id}/pending` with `CountBadge.Text(NeedsYou for this id)` from `MultisigAttentionState` and an `aria-label` that states the count, slot 5 More unchanged; show the name tab (`data-testid="nav-shared-tab"`, key icon, "*name* · *M-of-N*", opens `SharedWalletSwitcher`); set the `nav` `aria-label` from `nav.shared.label`; mark the current tab from `Section`; add `data-testid` values `nav-overview`, `nav-pending`, `nav-pending-count`; load the shared wallet's config with `GetConfigAsync` when the id changes and fall back to the personal menu when it cannot be read; subscribe to and unsubscribe from `MultisigAttentionState.Changed`
- [X] T023 [US1] In `OnScannedAsync` of src/DigiByte.Web/Layout/NavMenu.razor pass the shared wallet id to `ScanRouter.Route`, and when a Digi-ID code is scanned in shared mode show the `scan.leaving_shared` notice before navigating (FR-002a and its edge case)
- [X] T024 [US1] Make src/DigiByte.Web/Pages/MultisigSend.razor read optional `address` and `amount` query values with `[SupplyParameterFromQuery]`, feed them through the existing destination and amount checks on first load only, and, when `MultisigAccessNotice` is shown instead of the form, offer "Pay from *personal wallet*" linking to `/send?address=…&amount=…`; replace `WalletChip` with `SharedWalletContextLine`
- [X] T025 [US1] Update src/DigiByte.Web/Pages/MultisigDetail.razor: show `SharedWalletContextLine` under the title, remove the Scan, Pending and Activity tiles and their now-unused handlers and scanner so only Send and Receive remain (FR-007), keep the "needs your signature" banner and "View all" link
- [X] T026 [P] [US1] Replace `WalletChip` with `SharedWalletContextLine` in src/DigiByte.Web/Pages/MultisigActivity.razor
- [X] T027 [P] [US1] Replace `WalletChip` with `SharedWalletContextLine` in src/DigiByte.Web/Pages/MultisigPending.razor (header only; the list itself changes in US2)
- [X] T028 [US1] Make switching the active personal wallet end shared mode: in src/DigiByte.Web/Layout/NavMenu.razor, keep the active wallet id last seen and, when `AppState.OnChange` fires with a different one while `ShellContext` is Shared, navigate to `/` (FR-008)
- [ ] T029 [US1] Run quickstart section 1 in the preview at 375 px in light and dark themes and fix what it finds

**Checkpoint**: The shared menu, context line, switcher and scan routing work on their own

---

## Phase 4: User Story 2 - See what needs me in Pending and act with one tap (Priority: P1)

**Goal**: Pending is a grouped list of compact cards with one button each; every action on a payment lives on that payment's own screen; nothing irreversible happens from the list.

**Independent Test**: Quickstart section 2. With payments in each state, confirm the group order, one button per card, and that every existing action still completes from the payment's screen.

### Tests for User Story 2 (write first, must fail)

- [X] T030 [P] [US2] Add failing tests for `PendingGrouping.Group(items, localKey)` (a live room does not change the group: it follows who still has to sign) in tests/DigiByte.Wallet.Tests/MultisigUxPendingGroupingTests.cs: NeedsYou holds needs-your-signature, ready to broadcast, failed broadcast and a live-room item the viewer has not signed; WaitingForOthers holds you-signed, view-only waiting and a live-room item the viewer has signed; Cancelled holds cancelled; `Broadcast` items appear in no group; each group is newest first; empty input gives three empty groups
- [X] T031 [P] [US2] Add failing tests for `PendingCardActions.For(state, coinsSpent, isViewOnly)` in tests/DigiByte.Wallet.Tests/MultisigUxCardActionTests.cs covering the table in research.md R5: NeedsYourSignature → ReviewAndSign "Review & sign"; LiveRoom → OpenRoom "Open room"; YouSigned → GetSignatures "Get signatures"; WaitingForOthers → View "View"; ReadyToBroadcast → ReviewAndBroadcast "Review & broadcast"; Failed → ReviewAndRetry "Review & retry"; any open state with `coinsSpent` → ReviewAndCancel "Review & cancel"; CancelledOrExpired and Sent → None; a view-only viewer never gets ReviewAndSign or GetSignatures

### Implementation for User Story 2

- [X] T032 [P] [US2] Implement `PendingGrouping` in src/DigiByte.Wallet/Services/PendingGrouping.cs so T030 passes
- [X] T033 [P] [US2] Implement `PendingCardActions` in src/DigiByte.Wallet/Services/PendingCardActions.cs so T031 passes
- [X] T034 [P] [US2] Create `StickyAction` in src/DigiByte.Web/Components/Multisig/StickyAction.razor per contracts/ui-contracts.md (label, busy label, disabled reason shown as text, tone, optional child content for `SlideToConfirm`), pinned to the bottom edge with safe-area padding and a spacer so content is not covered, with its styles in src/DigiByte.Web/wwwroot/css/app.css
- [X] T035 [P] [US2] Create `PendingCard` in src/DigiByte.Web/Components/Multisig/PendingCard.razor: `StatusBadge`, "Received from a co-signer" label, amount, short recipient, `SignerProgress`, age, the spent-coins line, a second line "Your signature is needed" under the status when the state is Live room and the viewer has not signed, a one-line "View only: this device holds no key for this wallet" note for a view-only copy, and exactly one button from `PendingCardActions`; the card body raises `OnOpen`; no sign, slide, retry or cancel control
- [X] T036 [P] [US2] Create `AddProposalSheet` in src/DigiByte.Web/Components/Multisig/AddProposalSheet.razor: Scan QR, Paste, Choose file and (when `CanJoinRoom`) Join a room with a code, each with a one-line explanation; focus trapped, Esc closes
- [X] T037 [US2] Create src/DigiByte.Web/Pages/MultisigPendingDetail.razor at `/multisig/{WalletId}/pending/{PendingId}`: `SharedWalletContextLine`, Back to Pending, status, full `PaymentSummary` with the check-before-you-sign cue, `SignerProgress`, and a "no longer here" message with a way back when the payment is missing, cancelled-and-removed or already sent
- [X] T038 [US2] Move the sign flow (confirm step restating amount and recipient, `ConfirmSign`, busy and double-submit guards, spent-coins check) from src/DigiByte.Web/Pages/MultisigPending.razor into src/DigiByte.Web/Pages/MultisigPendingDetail.razor unchanged, shown through `StickyAction` when the action is ReviewAndSign
- [X] T039 [US2] Move slide-to-broadcast (`SlideToConfirm` with its attempt key) and Retry broadcast with their handlers from src/DigiByte.Web/Pages/MultisigPending.razor into src/DigiByte.Web/Pages/MultisigPendingDetail.razor unchanged; on success show a confirmation with the transaction ID, a copy action and a link to `/multisig/{WalletId}/activity`, then leave the detail screen (FR-018a)
- [X] T040 [US2] Move "Sign together online" (availability check, room length, name, open or join existing room), the Share panel (Copy PSBT, Save as file, QR or the too-large note) and the "Add a signed copy" panel (paste, scan, file, merge) with their handlers from src/DigiByte.Web/Pages/MultisigPending.razor into src/DigiByte.Web/Pages/MultisigPendingDetail.razor as labelled sections below the main button, each with a one-line explanation; "Get signatures" scrolls to them
- [X] T041 [US2] Move Cancel (with its confirmation text) and "Remove from list" for a cancelled item from src/DigiByte.Web/Pages/MultisigPending.razor into src/DigiByte.Web/Pages/MultisigPendingDetail.razor, visually separated as destructive; when the coins are already spent make Cancel the main action and hide sign and send (FR-025)
- [X] T042 [US2] Rebuild the list in src/DigiByte.Web/Pages/MultisigPending.razor: headings "Needs you", "Waiting for others" and a folded "Cancelled" from `PendingGrouping` (empty groups omitted), `PendingCard` per item with `CoinsSpent` from the list's existing `GetSpentInputIdsAsync` lookup (kept on this page), card and button both navigating to the detail address (Open room navigates to the room), one "Add a proposal" button at the top opening `AddProposalSheet`, the empty state with "Create a payment" and "Add a proposal", "Clear all cancelled" kept inside the Cancelled group, and the existing restore-cancelled and other-wallet prompts shown inside the sheet flow
- [X] T043 [US2] Keep the arrival flows working in src/DigiByte.Web/Pages/MultisigPending.razor: a proposal handed over by `ProposalHandoff` or `?scanned=` is received as today and then highlighted in its group; a newly received proposal offers "Review" leading to its detail screen
- [X] T044 [US2] Replace the inline green and red banners on src/DigiByte.Web/Pages/MultisigPending.razor and src/DigiByte.Web/Pages/MultisigPendingDetail.razor with `NotificationService` messages: success messages dismiss themselves, failures stay until dismissed; if src/DigiByte.Web/Services/NotificationService.cs has no persistent option, add one (FR-024)
- [X] T045 [US2] Point every link that used to open the Pending list for one payment at its detail address: the room's "Go to Pending" in src/DigiByte.Web/Pages/MultisigSigningRoom.razor, the created-payment step in src/DigiByte.Web/Pages/MultisigSend.razor, and pending entries opened from src/DigiByte.Web/Pages/MultisigActivity.razor and src/DigiByte.Web/Pages/MultisigDetail.razor
- [X] T046 [US2] Keep the person's place in the list: in src/DigiByte.Web/Pages/MultisigPending.razor remember the scroll offset of `#main-content` (in memory, per shared wallet) when a payment's screen is opened and restore it after the list renders on return, so Back lands where they left (Story 2 scenario 5); no restore on a fresh visit from the menu
- [ ] T047 [US2] Run quickstart section 2 and the Pending rows of specs/026-multisig-wallet-experience/checklists/existing-actions.md in the preview at 375 px; fix what they find

**Checkpoint**: Pending and the payment screen work on their own; the list cannot sign, send or retry

---

## Phase 5: User Story 3 - Understand the signing room at a glance and finish in it (Priority: P1)

**Goal**: The room shows the payment once, a three-step tracker, a pinned main button with the menu hidden, the invite where the host needs it, and a clear way out.

**Independent Test**: Quickstart section 3. Run a room on three profiles through invite, sign, decline, threshold, broadcast, disconnect and expiry; the tracker, headline, button and signer list match on every device and the button never needs scrolling.

### Tests for User Story 3 (write first, must fail)

- [X] T048 [P] [US3] Add failing tests in tests/DigiByte.Wallet.Tests/MultisigUxRoomStageTests.cs for `RoomStage.For(phase, presentOrSigned, signed, required)` (Invite while presentOrSigned < required; Sign once reached; Broadcast when signed ≥ required or phase Complete; Done once the payment is sent; a room that opens already at the threshold starts on Broadcast) and `RoomStage.InviteProminent(isHost, presentOrSigned, required, phase)` (true only for the host, room open, presentOrSigned < required)
- [X] T049 [P] [US3] Add failing tests in tests/DigiByte.Wallet.Tests/MultisigUxSignerOrderTests.cs for `SignerOrder.Sort(cards)`: the viewer first, then signed, then online, then offline; order inside each group is the input order; declined signers sort by their presence
- [X] T050 [P] [US3] Add failing tests in tests/DigiByte.Wallet.Tests/MultisigUxRoomCountdownTests.cs for `RoomCountdown.For(remaining)`: text as `mm:ss` under an hour and `h:mm:ss` above; `IsUrgent` true at exactly 5:00 and below, false above; zero or negative shows `0:00` and is urgent
- [X] T051 [P] [US3] Update tests/DigiByte.Wallet.Tests/MultisigUxRoomHeadlineTests.cs first (failing) for the new wording of `SigningRoomHeadline`: the can't-complete detail reads "Too many co-signers declined. You can cancel it from the payment's screen." and its action label is "Open payment" (the action stays `PrimaryAction.GoToPending`); no headline text says "Pending list"

### Implementation for User Story 3

- [X] T052 [P] [US3] Implement `RoomStage` in src/DigiByte.Wallet/Services/RoomStage.cs so T048 passes
- [X] T053 [P] [US3] Implement `SignerOrder` in src/DigiByte.Wallet/Services/SignerOrder.cs so T049 passes
- [X] T054 [P] [US3] Implement `RoomCountdown` in src/DigiByte.Wallet/Services/RoomCountdown.cs so T050 passes
- [X] T055 [P] [US3] Create `RoomBar` in src/DigiByte.Web/Components/Multisig/RoomBar.razor: invite code, time left (emphasised with icon and text when urgent, announced once through a polite live region), connection state, and a labelled menu button; sticky at the top of the room
- [X] T056 [P] [US3] Create `RoomStageTracker` in src/DigiByte.Web/Components/Multisig/RoomStageTracker.razor: Invite, Sign, Broadcast with done, current and upcoming shown by icon and text, and a text alternative such as "Step 2 of 3: Sign"
- [X] T057 [P] [US3] Create `RoomInvite` in src/DigiByte.Web/Components/Multisig/RoomInvite.razor: prominent form (code, Share, Copy link, Copy code, QR) and folded form (one row with the code and an expand control); Share uses the device share sheet when available and otherwise copies the link
- [X] T058 [P] [US3] Create `RoomMenu` in src/DigiByte.Web/Components/Multisig/RoomMenu.razor: Leave ("signatures already collected stay on this device") and, for the host only, Close room ("ends the room for everyone; the payment is not cancelled") with a confirm step; focus trapped, Esc closes
- [X] T059 [P] [US3] Create `RoomCodeInput` in src/DigiByte.Web/Components/Multisig/RoomCodeInput.razor: six characters, upper-cased, a full paste fills all six, one labelled input for assistive technology, and a scan button
- [X] T060 [US3] Add the room styles (room bar, tracker, folded invite) to src/DigiByte.Web/wwwroot/css/app.css with no animation under `prefers-reduced-motion`
- [X] T061 [US3] Hide the menu in the room: in src/DigiByte.Web/Pages/MultisigSigningRoom.razor call `ShellChrome.SetHideNav(true)` when the phase becomes `InRoom` or `Ended` and `false` on every other phase and on dispose; in src/DigiByte.Web/Layout/MainLayout.razor make `ShowNav` also require `!Chrome.HideNav` and re-render on `ShellChrome.Changed` (FR-028)
- [X] T062 [US3] Rebuild the `InRoom` layout in src/DigiByte.Web/Pages/MultisigSigningRoom.razor in this order: `RoomBar`, `SharedWalletContextLine`, `ConnectionBanner`, `RoomStageTracker`, headline (title and detail from the existing `SigningRoomHeadline`), `RoomInvite` (prominent or folded from `RoomStage.InviteProminent`), one `PaymentSummary` with amount, recipient and total visible and the rest under Details, the signer list with "X of Y signatures · N more needed" using `SignerOrder`, the existing one-line view-only explanation for a view-only copy; remove the duplicate amount card and the bottom Close room / Leave links
- [X] T063 [US3] Move the room's main button into `StickyAction` in src/DigiByte.Web/Pages/MultisigSigningRoom.razor (Review & sign, Broadcast, Open payment), keeping the existing busy labels and the "Waiting for the connection to come back" disabled reason; keep "Decline to sign" beside the review with its confirmation; keep the confirm-sign step restating amount and recipient
- [X] T064 [US3] Wire `RoomMenu` in src/DigiByte.Web/Pages/MultisigSigningRoom.razor to the existing `LeaveAsync` and `CloseAsync` handlers, and use `RoomCountdown` for the time left with the once-only announcement at 5 minutes; if the time runs out while the confirm step is open, close it with an explanation
- [X] T065 [US3] Rebuild the `Ended` result screen in src/DigiByte.Web/Pages/MultisigSigningRoom.razor: what happened, signatures kept, transaction ID with a copy action when sent, and one button to the payment's detail screen (or the list when the payment is gone); the menu returns when the person leaves
- [X] T066 [US3] Rebuild the `Join` screen in src/DigiByte.Web/Pages/MultisigSigningRoom.razor with `RoomCodeInput`, a line stating which wallet the person is joining as, the join button enabled only for a complete code and name, and the existing "try with another wallet" choices; the personal menu stays visible here
- [X] T067 [US3] Replace the wording that sends people to "the Pending list" for actions that now live on the payment's own screen: `SigningRoomHeadline` in src/DigiByte.Wallet/Services/SigningRoomHeadline.cs (so the updated headline tests pass) and the six strings in src/DigiByte.Web/Pages/MultisigSigningRoom.razor (unavailable notice, ended notice, spent-coins notice and the related error messages), each naming "the payment's screen" or "Pending" as is now true
- [ ] T068 [US3] Run quickstart section 3 on three browser profiles and the room rows of specs/026-multisig-wallet-experience/checklists/existing-actions.md; fix what they find

**Checkpoint**: The room works on its own on three devices

---

## Phase 6: User Story 4 - Read the health of every shared wallet from the list (Priority: P2)

**Goal**: Each card shows balance, one status line, backup and view-only state; wallets that need the person come first; Create and Import are both visible.

**Independent Test**: Quickstart section 4. With shared wallets in different states, each card states its condition correctly and attention items are first.

### Tests for User Story 4 (write first, must fail)

- [ ] T069 [P] [US4] Add failing tests in tests/DigiByte.Wallet.Tests/MultisigUxWalletCardTests.cs for `WalletCardStatus.For(pending, localKey, liveRoomTxId)` returning a `WalletCardStatusLine`: "live room" only when `liveRoomTxId` (the room this device has joined) matches; urgency order needs your signature (with count) > ready to broadcast (with count) > live room > waiting for others (count of co-signers still to sign on the newest open payment) > none; cancelled and sent items are ignored; a view-only viewer never gets needs-your-signature
- [ ] T070 [P] [US4] Add failing tests in the same file tests/DigiByte.Wallet.Tests/MultisigUxWalletCardTests.cs for `SharedWalletOrder.Sort(models)`: wallets with `NeedsYou > 0` first, then by `LastActivity` newest first, ties keep input order; `LastActivity` is the latest of the newest pending `UpdatedAt` and the wallet's `CreatedAt`

### Implementation for User Story 4

- [ ] T071 [P] [US4] Implement `WalletCardStatus` in src/DigiByte.Wallet/Services/WalletCardStatus.cs so T069 passes
- [ ] T072 [P] [US4] Implement `SharedWalletOrder` in src/DigiByte.Wallet/Services/SharedWalletOrder.cs so T070 passes
- [ ] T073 [US4] Create `MultisigWalletCard` in src/DigiByte.Web/Components/Multisig/MultisigWalletCard.razor: name, scheme chip, co-signer count, balance in DGB and the person's currency in a fixed-height area (loading placeholder, amount, "Unavailable", or hidden), one status line with icon and text, "Not backed up" and "View only" marks with icon and text, the whole card opens the wallet, and a "more" button offering Rename
- [ ] T074 [US4] Rebuild src/DigiByte.Web/Pages/MultisigWallets.razor: heading "Shared wallets in *wallet*" with the wallet's colour, Create and Import as two labelled buttons, cards built from saved data and ordered once with `SharedWalletOrder` per load, the drafts and unassigned sections restyled to match, and an empty state with one sentence and both actions
- [ ] T075 [US4] In src/DigiByte.Web/Pages/MultisigWallets.razor load balances after first render with `MultisigWalletService.GetBalanceAsync` for every card in parallel, filling each card as its call returns and marking it "Unavailable" on failure; fetch the price once with `GetDgbPriceAsync(AppState.FiatCurrency)`; read `pref:hideBalance` and hide balances the same way `MultisigBalanceCard` does; never reorder after balances arrive
- [ ] T076 [US4] Run quickstart section 4 (including offline and hide-balance) in the preview at 375 px in both themes, note the measured time for the cards to appear (SC-011: within 1 second) in the pull request; fix what it finds

**Checkpoint**: The list works on its own

---

## Phase 7: User Story 5 - Get back to my shared wallet in one tap (Priority: P3)

**Goal**: A waiting payment is visible from every personal screen, and the home shortcut reopens the last-used shared wallet.

**Independent Test**: Quickstart section 5. The same count shows on More, the Multisig row and the home shortcut; the shortcut opens the last-used shared wallet, the only one, or the list.

### Tests for User Story 5 (write first, must fail)

- [ ] T077 [P] [US5] Add failing tests for `SharedWalletShortcut.Target(lastUsedId, availableIds)` in tests/DigiByte.Wallet.Tests/MultisigUxShortcutTests.cs: last used when it is in `availableIds`; the single id when there is exactly one and no valid last used; null (open the list) for none, for several without a valid last used, and for a last-used id that is no longer available with several others

### Implementation for User Story 5

- [ ] T078 [P] [US5] Implement `SharedWalletShortcut` in src/DigiByte.Wallet/Services/SharedWalletShortcut.cs so T077 passes
- [ ] T079 [P] [US5] Add English and Spanish text for `nav.count.more` ("{0} multisig payments need you", with a singular form) and `more.item.multisig.count` to src/DigiByte.Web/wwwroot/locales/en.json and src/DigiByte.Web/wwwroot/locales/es.json
- [ ] T080 [US5] In src/DigiByte.Web/Layout/NavMenu.razor show `CountBadge.Text(MultisigAttentionState.Current NeedsYou total)` on the More tab in personal mode (`data-testid="nav-more-count"`, count in the tab's `aria-label`) and on the Multisig row inside the More list; no count in shared mode (FR-010a)
- [ ] T081 [US5] Record the last-used shared wallet: when src/DigiByte.Web/Pages/MultisigDetail.razor loads a config, write its id to `pref:multisigLastUsed:{personalWalletId}` through `ISecureStorage`, ignoring storage failures
- [ ] T082 [US5] In src/DigiByte.Web/Pages/Home.razor show the `NeedsYou` total on the Multisig shortcut tile and make `OnMultisig` read the last-used id, list the active wallet's shared wallets, and navigate to `SharedWalletShortcut.Target(...)` or to `/multisig` when it returns null; More → Multisig keeps going to `/multisig`
- [ ] T083 [US5] Run quickstart section 5 in the preview, including signing a waiting payment and watching every count drop without a reload; fix what it finds

**Checkpoint**: All five stories work

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Things that span stories

- [ ] T084 [P] Update the menu section of docs/UI_STYLE.md to describe shared-wallet mode (the five slots, the name tab, counts, the room hiding the menu)
- [ ] T085 [P] Remove what the feature made unused after confirming no references remain: `WalletChip` in src/DigiByte.Web/Components/Multisig/WalletChip.razor if no page uses it, dead handlers and fields left in src/DigiByte.Web/Pages/MultisigPending.razor and src/DigiByte.Web/Pages/MultisigDetail.razor
- [ ] T086 [P] Update the multisig help text that names the old screens (five Pending buttons, dashboard tiles) in src/DigiByte.Web/Pages/MultisigGuide.razor and src/DigiByte.Web/Pages/Help.razor
- [ ] T087 Accessibility pass over every changed screen in the preview: keyboard only, focus order and trap in sheets, status and count announcements, the 5-minute warning, 44 px targets, large text, contrast in both themes, reduced motion (FR-036, SC-013)
- [ ] T088 Check the wide-screen side-rail layout of the menu in shared mode and with counts, and the pinned action on an installed PWA on Android and iOS (system bars, on-screen keyboard)
- [ ] T089 Switch the app to Spanish and confirm every new menu label and count reads in Spanish with no raw keys (FR-037)
- [ ] T090 Walk the full specs/026-multisig-wallet-experience/checklists/existing-actions.md and quickstart section 6 end to end; record the result in that checklist (SC-012)
- [ ] T091 Run the four moderated checks from the spec with at least five people before release (SC-002 name both wallets in 5 seconds, SC-006 pick the right next action for five card states, SC-007 say what the room is waiting for within 10 seconds, SC-010 read the list's states) and record the results in specs/026-multisig-wallet-experience/checklists/moderated-checks.md
- [ ] T092 Run `dotnet build DigiByte.slnx` and `dotnet test` for the whole solution and confirm both pass
- [ ] T093 Add the user-facing changes to CHANGELOG.md and the "What's New" list in src/DigiByte.Web/Layout/MainLayout.razor

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: after Setup; blocks every story
- **US1 (Phase 3)**: after Foundational
- **US2 (Phase 4)**: after Foundational; uses `SharedWalletContextLine` from US1 on the new detail page (T037). If US2 is built before US1, keep `WalletChip` there and swap it when US1 lands
- **US3 (Phase 5)**: after Foundational, and in practice after US1 and US2; uses `StickyAction` (T034) from US2 and `SharedWalletContextLine` (T020) from US1; T065 links to the detail page from US2 and falls back to the list until it exists
- **US4 (Phase 6)**: after Foundational; independent of US1 to US3
- **US5 (Phase 7)**: after Foundational; T080 edits the same file as T022, so do it after US1
- **Polish (Phase 8)**: after the stories being shipped

### Within Each Story

- Tests first, and failing, before the logic they cover
- `MultisigUxModels.cs` types (T006) before any logic that returns them
- Logic before components; components before the page that uses them
- The handler moves T038 to T041 touch the same two files and run in order
- The room page tasks T061 to T067 touch the same file and run in order

### Parallel Opportunities

- Phase 2: T003, T004, T005 together; then T007 and T010 alongside T008 and T009
- US1: T013 and T014 together; T017, T018, T019 together; T026 and T027 together
- US2: T030 and T031 together; T032 to T036 together
- US3: T048 to T051 together; T052 to T059 together
- US4: T069 and T070 are in one file (run together by one person); T071 and T072 together
- US4 can be built by a second person at any time after Phase 2
- Polish: T084, T085, T086 together

---

## Parallel Example: User Story 2

```text
# Tests first, together:
Task: "Failing tests for PendingGrouping in tests/DigiByte.Wallet.Tests/MultisigUxPendingGroupingTests.cs"
Task: "Failing tests for PendingCardActions in tests/DigiByte.Wallet.Tests/MultisigUxCardActionTests.cs"

# Then logic and components, together:
Task: "Implement PendingGrouping in src/DigiByte.Wallet/Services/PendingGrouping.cs"
Task: "Implement PendingCardActions in src/DigiByte.Wallet/Services/PendingCardActions.cs"
Task: "Create StickyAction in src/DigiByte.Web/Components/Multisig/StickyAction.razor"
Task: "Create PendingCard in src/DigiByte.Web/Components/Multisig/PendingCard.razor"
Task: "Create AddProposalSheet in src/DigiByte.Web/Components/Multisig/AddProposalSheet.razor"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 and Phase 2
2. Phase 3 (US1): shared menu, context line, switcher, scan routing
3. Stop and check with quickstart section 1; this alone fixes "people lose their place"

### Incremental Delivery

1. US1 → check → ship
2. US2 → check → ship (largest change: the Pending split)
3. US3 → check → ship
4. US4 → check → ship
5. US5 → check → ship
6. Polish, then the moderated checks named in the spec (SC-002, SC-006, SC-007, SC-010) before release

Each story is one pull request with its tests, using Conventional Commits.

### Two People

After Phase 2: one person takes US1 then US2 then US3 (they share components and the menu); the other takes US4, then US5 once US1 has merged.

---

## Notes

- Move existing handlers, do not rewrite them; their guards are the safety net
- No task changes a hub, a wire contract, signing, signature checks or stored payment data
- Only `address`, `amount`, `code` and an opaque pending id may appear in an address
- Commit after each task or small group; stop at any checkpoint to check the story on its own
