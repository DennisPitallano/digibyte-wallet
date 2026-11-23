# Research: Multisig Wallet Experience

Decisions taken from reading the current code. Nothing in the Technical Context is left open.

## R1. How the menu knows it is inside a shared wallet

**Decision**: Derive it from the address. A pure function `ShellContext.Resolve(path)` in `DigiByte.Web/Layout` returns `Personal` or `Shared(walletId)`. `Shared` applies to `multisig/{id}` and its sub-paths (`send`, `pending`, `pending/{pendingId}`, `activity`), where `{id}` is not one of the reserved words `create`, `import`, `room`, `sign-room`. `NavMenu` already recomputes on every `LocationChanged`.

**Rationale**: The address is the single source of truth, so the menu cannot disagree with the page, a reload or a deep link gives the right menu at once, and the rule is unit-testable (`ScanRouterTests` shows the test project already covers `DigiByte.Web` services).

**Alternatives considered**: A scoped "current shared wallet" service that pages set and clear. Rejected: every page would have to remember to set and clear it, and a missed clear leaves the wrong menu on a personal screen.

## R2. Hiding the menu in the signing room

**Decision**: A small scoped service `ShellChrome` with `HideNav` and a `Changed` event. `MultisigSigningRoom` sets it while its phase is `InRoom` or `Ended`, and clears it on `Join`, `Connecting`, `Unavailable` and on dispose. `MainLayout.ShowNav` becomes `!HiddenNavPaths.Contains(path) && !Chrome.HideNav`.

**Rationale**: The room has one fixed address (`/multisig/sign-room`) for join, live room and result, so the address alone cannot tell them apart (FR-028 keeps the menu on the join screen). The layout already switches `main-with-nav` / `pb-4` on `ShowNav`, so the page's bottom padding follows for free.

**Alternatives considered**: Adding the room to `HiddenNavPaths`. Rejected: it would also hide the menu on the join screen. Splitting the room into three routes. Rejected: larger change to a working page and its unlock-redirect logic.

## R3. Counts on the menu, the More row and the home shortcut

**Decision**: One scoped service in `DigiByte.Web`, `MultisigAttentionState`, holds the current `AttentionSummary` for the active wallet's shared wallets and raises `Changed`. It refreshes when the active wallet changes, when the app unlocks, and when `MultisigWalletService` raises a new `PendingChanged` event (fired from the three places that write pending items: `SavePendingTransactionAsync`, `RemoveCancelledAsync`, `DeleteWalletAsync`). `NavMenu`, `Home`, the list and the dashboard read from it.

`AttentionCounter` gains a second figure, "needs you" (needs my signature + ready to broadcast + failed broadcast), next to the existing needs-signature figure, so the list's "N payments need your signature" line keeps its meaning while the menu counts match the Pending group (FR-004, FR-010a).

**Rationale**: Counts come from items already saved on the device (as spec 018 decided), so there are no network calls. One owner of the number means the More tab, the Multisig row, the home shortcut and the Pending tab can never disagree.

**Alternatives considered**: Each surface loading its own count (today's approach on two pages). Rejected: four surfaces would drift and re-read storage four times.

## R4. The pending payment's own screen

**Decision**: A new page `MultisigPendingDetail.razor` at `/multisig/{WalletId}/pending/{PendingId}`. Sign, slide to broadcast, retry, share, add a signed copy, open a room and cancel move there from `MultisigPending.razor` with their existing handlers. The list keeps: grouping, cards, "Add a proposal", "Clear all cancelled", and the scan/handoff arrival flow. A room's "Go to Pending" and a successful proposal receive link to the detail address.

The pending id is an opaque local id, not sensitive, so it may appear in the address; proposals and redeem scripts still never do (`ProposalHandoff`, `ShellActions` unchanged).

**Rationale**: Matches the clarified decision; gives Back a natural meaning; removes about two thirds of the 960-line list page into a focused page; the shared context (R1) covers it without extra work.

**Alternatives considered**: A sheet over the list (rejected in clarification).

## R5. Which button a card shows

**Decision**: A pure mapper in `DigiByte.Wallet`, `PendingCardActions.For(state, coinsSpent, isViewOnly)`, returns one of: `ReviewAndSign`, `OpenRoom`, `GetSignatures`, `ReviewAndBroadcast`, `ReviewAndRetry`, `ReviewAndCancel`, `View`, or `None` (cancelled). `MultisigStateVocabulary` keeps its state labels and icons; its action labels stay for the screens that still use them.

| State | Card button |
|---|---|
| Needs your signature | Review & sign |
| Live room | Open room |
| You signed | Get signatures |
| Waiting for others (view only) | View |
| Ready to broadcast | Review & broadcast |
| Failed | Review & retry |
| Coins already spent (any open state) | Review & cancel |
| Cancelled | none |

**Rationale**: FR-019/FR-019a need exactly one button per card and nothing irreversible on the list. A table in one function is testable and keeps wording in one place.

## R6. Grouping and ordering

**Decision**: Pure functions in `DigiByte.Wallet`:

- `PendingGrouping.Group(items, localKey, liveRoomTxId)` → `NeedsYou`, `WaitingForOthers`, `Cancelled`, each newest first; a live-room item the viewer has not signed counts as `NeedsYou`.
- `SharedWalletOrder.Sort(wallets)` → needs-you first, then by latest of (newest pending `UpdatedAt`, `CreatedAt` of the wallet). Computed once per list load, so the order never changes while the list is on screen (FR-015).
- `WalletCardStatus.For(pending, localKey, liveRoomTxId)` → the one status line, by urgency (FR-012).

**Rationale**: "Most recent activity" from saved data only (spec assumption); no new tracking.

## R7. Balances on the list

**Decision**: After the cards render from saved data, the list calls the existing `MultisigWalletService.GetBalanceAsync(config)` for each shared wallet in parallel and fills each card as its call returns; a failure shows "Unavailable". Fiat uses the same `GetDgbPriceAsync(AppState.FiatCurrency)` call the dashboard makes, once per list load. Hide-balance reads the same `pref:hideBalance` key `MultisigBalanceCard` uses. Balances are not cached to storage.

**Rationale**: Same data source and same privacy posture as the dashboard: the list reveals to the backend exactly the addresses the dashboards would. Cards reserve the balance line's height so nothing moves when it fills (SC-011).

**Alternatives considered**: Caching last balances in storage for instant display. Rejected for now: it would be new stored data (FR-038) for a small gain; can be revisited.

## R8. Scan inside a shared wallet

**Decision**: `ScanRouter.Route` gets an optional `sharedWalletId`. For `Payment` it then returns `/multisig/{id}/send?address=…&amount=…` in place of `/send?…`. `NavMenu` passes the id from R1. `MultisigSend` reads `address` and `amount` from the query (as the personal Send already does) and runs them through its existing destination and amount checks. If the shared wallet cannot be opened there (`MultisigAccessNotice` states), that screen offers "Pay from *personal wallet*", which links to `/send?…` with the same values.

**Rationale**: Only an address and an amount travel in the address, as for the personal Send today. Other scan kinds are untouched. A watch-only copy can already create an unsigned proposal in `MultisigSend`, so "cannot create payments" reduces to "cannot open this shared wallet".

## R9. Last-used shared wallet

**Decision**: Stored with the existing preference pattern as `pref:multisigLastUsed:{personalWalletId}` = shared wallet id, written when a dashboard opens. A pure function `SharedWalletShortcut.Target(lastUsedId, availableIds)` returns the id to open or null for "open the list" (last used if still available; else the only one; else null). `Home.OnMultisig` uses it; More → Multisig always goes to `/multisig`.

**Rationale**: One small preference, device-local, no secret (FR-038 names it as the only new saved item).

## R10. How the menu looks different in shared mode (FR-003)

**Decision**: Three cues together: (1) a slim tab attached to the top edge of the bar with a key icon and the shared wallet's name and scheme ("HalfThePot · 3-of-5"), which is also a button that opens the switcher; (2) the labels themselves (Overview, Pending); (3) the pill accent stays the personal wallet's colour, matching the "in *wallet*" colour of the context line. The `nav` element's accessible name becomes "*name* shared wallet navigation".

**Rationale**: Not colour alone; the name is where the thumb is, so the mode is visible without looking at the header; reuses `--nav-accent`. On the side-rail layout (wide screens) the tab sits above the rail.

**Alternatives considered**: A different bar colour only (fails FR-003); replacing the Scan disc icon (weakens the one-scanner promise).

## R11. Room stage, invite placement, signer order, countdown

**Decision**: Pure functions in `DigiByte.Wallet`:

- `RoomStage.For(phase, presentOrSigned, signed, required)` → `Invite` (presentOrSigned < required), `Sign`, `Broadcast` (signed ≥ required), `Done` (sent).
- `RoomStage.InviteProminent(isHost, presentOrSigned, required, phase)` (not a separate `RoomInvite` class: that name belongs to the component).
- `SignerOrder.Sort(cards)` → you, signed, online, offline; stable inside each group. Applied after the existing `SignerCards`/`PresenceDebouncer`, so presence smoothing is unchanged.
- `RoomCountdown.For(remaining)` → text, `IsUrgent` at ≤ 5 minutes, and a one-time announce flag held by the page.

The existing `SigningRoomHeadline` and `RoomHeadlineCard` are kept; the card's button moves into a new `StickyAction` bar pinned to the bottom edge (safe-area aware), and the headline keeps the title and detail.

**Rationale**: All inputs already arrive in `SigningRoomView`; nothing new is sent or received (FR-038).

## R12. Messages that do not push the list

**Decision**: Use the existing `NotificationService` / `ToastContainer` (already mounted in `MainLayout`) for results on Pending and the detail page. Success toasts auto-dismiss; failures use the persistent variant. If the service has no persistent variant, add one option flag there. The inline green/red banners on `MultisigPending` are removed.

**Rationale**: FR-024; the toast region is already an announced live region.

## R13. Translations

**Decision**: New keys in `wwwroot/locales/en.json` and `es.json` for the menu only: `nav.overview`, `nav.pending`, `nav.shared.label`, `nav.shared.switch`, `nav.count.pending`, `nav.count.more`, `more.item.multisig.count`. `NavMenu.T(key, english)` already falls back to English. Multisig screens stay hard-coded English, as today.

## R14. Tests

**Decision**: All new decision logic gets xUnit tests written first in `tests/DigiByte.Wallet.Tests` (Principle III), including `ShellContext` and the `ScanRouter` change, which live in `DigiByte.Web` but are already tested from that project. No component-test project is added; screens are verified with the quickstart walkthrough in the preview at phone width, both themes, reduced motion.
