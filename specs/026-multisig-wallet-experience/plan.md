# Implementation Plan: Multisig Wallet Experience (List, Navigation, Pending, Signing Room)

**Branch**: `026-multisig-wallet-experience` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/026-multisig-wallet-experience/spec.md`

## Summary

A presentation and navigation pass over four multisig areas. Inside a shared wallet the bottom menu keeps its five
positions but points at that shared wallet (Overview, Activity, Scan, Pending, More), every screen carries a context
line naming the shared wallet and the personal wallet it belongs to, and a switcher gives one-tap exit. The Pending
list becomes a grouped list of compact cards with one button each; every action on a payment (sign, send, retry,
share, import, room, cancel) moves to a new screen for that one payment. The signing room shows the payment once,
a three-step tracker, a pinned main button with the menu hidden, and the invite where the host needs it. The list
shows balance, status, backup and view-only state per shared wallet. A count of payments that need the person shows
on More, the Multisig row and the home shortcut.

Approach: the menu mode is derived from the address by a pure function; one small service owns the counts; all
ordering, grouping and "which button" decisions are pure functions in `DigiByte.Wallet`, written test-first;
`DigiByte.Web` gets one new page and about eleven new components and rearranges four pages. No server, wire contract,
signing, verification or stored payment data changes. One device-local preference is added (last-used shared wallet).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), Blazor WebAssembly

**Primary Dependencies**: Existing only: Blazor, Tailwind 3.4 through the existing build, the SignalR client already wired in `SigningRoomClient`. No new packages.

**Storage**: Existing encrypted browser store through `ISecureStorage`. One new preference key, `pref:multisigLastUsed:{personalWalletId}`. No migration.

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` (which already references `DigiByte.Web`) for all decision logic. Screens are checked with the [quickstart](quickstart.md) walkthrough in the preview at phone width, both themes, reduced motion and keyboard. No component-test project exists and none is added.

**Target Platform**: Browser PWA, phone-first, light and dark themes; the side-rail menu layout on wide screens must also carry the shared mode.

**Project Type**: Web application (Blazor WASM client). `DigiByte.Api` and the hubs are not touched.

**Performance Goals**: List cards (name, scheme, status) visible within 1 s from saved data; balances fill in afterwards without layout shift (SC-011). Counts update without network calls. Menu mode switches in the same render as the navigation.

**Constraints**: Presentation and navigation only (FR-038); nothing new leaves the device; offline path unchanged; no irreversible control on the Pending list (FR-019a); status never by colour alone; touch targets at least 44 px; reduced motion respected; menu labels in English and Spanish only (FR-037).

**Scale/Scope**: 1 new page, about 11 new components, 11 new pure-logic units with tests, 2 small services, edits to 10 existing pages or components (`NavMenu`, `MainLayout`, `MoreMenu` row, `Home`, `MultisigWallets`, `MultisigDetail`, `MultisigPending`, `MultisigSigningRoom`, plus `MultisigSend` and `MultisigActivity` for the context line and scan prefill). Up to 7 co-signers per room; tens of pending items per shared wallet.

## Constitution Check

*GATE: passes before research; re-checked after design (still passes).*

| Principle | Status | Note |
|---|---|---|
| I. Self-custody | Pass | No key, seed or password handling is touched. The new preference holds only a shared wallet id. |
| II. In-browser crypto / relay limits | Pass | No server or hub change. The signing service receives exactly what it receives today. Signing still happens only on the device after an explicit confirm. |
| III. Test-first for Wallet logic | Pass, applies | Every new unit in `DigiByte.Wallet` (and the `PendingChanged` event, the extended `AttentionCounter`) gets failing tests first, in the same PR. `ShellContext` and the `ScanRouter` change are tested the same way from the existing test project. |
| IV. Boundary discipline | Pass | Decisions in `DigiByte.Wallet`; rendering, routing and shell services in `DigiByte.Web`; no new reference direction. |
| V. Resilience | Pass | Cards render from saved data; balances degrade to "Unavailable"; counts need no network; the room keeps its reconnect notice and the offline share/import path stays on the payment's screen. |
| Security: secrets and addresses | Pass | Only `address`, `amount`, `code` and an opaque pending id appear in addresses. Proposals and redeem scripts stay in memory hand-offs. The list makes the same balance calls the dashboards make, so no new address disclosure pattern. |
| Workflow: docs | Applies | `docs/UI_STYLE.md` (menu section) is updated to describe the shared mode. |

No violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/026-multisig-wallet-experience/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-contracts.md
├── checklists/requirements.md
└── tasks.md            # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Models/
│   └── MultisigUxModels.cs              # + PendingCardAction, PendingGroups, WalletCardModel, RoomStage enums/records
└── Services/
    ├── AttentionCounter.cs              # + NeedsYou (signature + ready to broadcast + failed broadcast)
    ├── MultisigWalletService.cs         # + PendingChanged event (3 write paths)
    ├── SigningRoomHeadline.cs           # wording: actions now live on the payment's screen
    ├── PendingGrouping.cs               # new
    ├── PendingCardActions.cs            # new
    ├── WalletCardStatus.cs              # new
    ├── SharedWalletOrder.cs             # new
    ├── SharedWalletShortcut.cs          # new
    ├── RoomStage.cs                     # new (stage, invite placement)
    ├── SignerOrder.cs                   # new
    ├── RoomCountdown.cs                 # new
    └── CountBadge.cs                    # new ("99+")

src/DigiByte.Web/
├── Layout/
│   ├── ShellContext.cs                  # new: path → Personal | Shared(id, section)
│   ├── NavMenu.razor                    # two modes, name tab, counts, scan passes shared id
│   ├── NavIcons.cs                      # + overview, pending
│   ├── MoreMenu.cs / NavMenu more list  # count on the Multisig row
│   └── MainLayout.razor                 # ShowNav also honours ShellChrome.HideNav
├── Services/
│   ├── ShellChrome.cs                   # new
│   ├── MultisigAttentionState.cs        # new
│   ├── NotificationService.cs           # + a message that stays until dismissed, if missing
│   └── ScanRouter.cs                    # optional sharedWalletId
├── Components/Multisig/
│   ├── SharedWalletContextLine.razor    # new (replaces WalletChip on multisig pages)
│   ├── SharedWalletSwitcher.razor       # new
│   ├── MultisigWalletCard.razor         # new
│   ├── PendingCard.razor                # new
│   ├── AddProposalSheet.razor           # new
│   ├── StickyAction.razor               # new
│   ├── RoomBar.razor                    # new
│   ├── RoomStageTracker.razor           # new
│   ├── RoomInvite.razor                 # new
│   ├── RoomMenu.razor                   # new
│   └── RoomCodeInput.razor              # new
├── Pages/
│   ├── MultisigWallets.razor            # redesigned list
│   ├── MultisigDetail.razor             # context line, two tiles, records last-used
│   ├── MultisigPending.razor            # grouped list only
│   ├── MultisigPendingDetail.razor      # new: one payment, all actions
│   ├── MultisigSigningRoom.razor        # redesigned room, hides menu
│   ├── MultisigSend.razor               # context line, address/amount from query
│   ├── MultisigActivity.razor           # context line
│   └── Home.razor                       # shortcut count and last-used target
├── Program.cs                           # register ShellChrome, MultisigAttentionState
└── wwwroot/
    ├── css/app.css                      # nav shared tab, sticky action, room bar, tracker
    └── locales/en.json, es.json         # new nav.* and more.* keys

tests/DigiByte.Wallet.Tests/
├── MultisigUxAttentionTests.cs          # extended
├── ScanRouterTests.cs                   # extended
├── ShellContextTests.cs                 # new
├── MultisigUxPendingGroupingTests.cs    # new
├── MultisigUxCardActionTests.cs         # new
├── MultisigUxWalletCardTests.cs         # new (status, order)
├── MultisigUxShortcutTests.cs           # new
├── MultisigUxRoomStageTests.cs          # new (stage, invite placement)
├── MultisigUxSignerOrderTests.cs        # new
├── MultisigUxRoomCountdownTests.cs      # new
├── MultisigUxCountBadgeTests.cs         # new
├── MultisigUxRoomHeadlineTests.cs       # updated wording
└── MultisigPendingChangedTests.cs       # new

docs/UI_STYLE.md                         # menu section: shared mode
```

**Structure Decision**: The existing two-project split is kept: `DigiByte.Wallet` for decisions, `DigiByte.Web` for screens and shell. No new project.

## Design Notes

Full reasoning is in [research.md](research.md); shapes are in [data-model.md](data-model.md); screens and components are in [contracts/ui-contracts.md](contracts/ui-contracts.md).

1. **Menu mode from the address** (R1). `ShellContext.Resolve` is the only place that knows which routes are "inside a shared wallet". `NavMenu` renders slot 1, 2 and 4 from it and passes the id to `ScanRouter`.
2. **Room hides the menu** (R2) through `ShellChrome`, because one route serves join, room and result.
3. **One owner of counts** (R3). `MultisigAttentionState` listens to the new `PendingChanged` event; four surfaces read it.
4. **Split Pending** (R4, R5, R6). The list keeps arrival and bulk actions; the detail page takes every per-payment handler with its existing guards. This is the largest move in the feature and should be done by relocating code, not rewriting it.
5. **Room layout** (R11). Reuse `SigningRoomHeadline`; add stage, invite placement, signer order and countdown as pure functions; pin the action with `StickyAction`.
6. **List** (R7). Render from saved data first; fill balances per card.

## Suggested Delivery Order

Each step is shippable on its own and matches a user story.

1. Shell: `ShellContext`, shared menu mode, context line, switcher, scan routing, dashboard tiles (Story 1).
2. Pending: grouping, card, detail page, add-proposal sheet, toasts (Story 2).
3. Room: room bar, tracker, sticky action, invite, room menu, join input, menu hidden (Story 3).
4. List: card model, ordering, balances (Story 4).
5. Counts and return: `MultisigAttentionState` on More, Multisig row, home shortcut; last-used (Story 5). The Pending tab count from step 1 uses the same service, so the service itself lands in step 1 and its other surfaces here.

## Risks

| Risk | Mitigation |
|---|---|
| Moving handlers out of `MultisigPending` drops a guard (double submit, spent coins, confirm step) | Move code unchanged; quickstart's existing-actions checklist (SC-012); existing service tests still cover the rules. |
| Menu mode and page disagree on an unusual address | Single pure resolver with table-driven tests, including reserved words and trailing slashes. |
| Sticky action covered by the system bar or keyboard | Use the same safe-area handling as the menu; check on an installed PWA on Android and iOS. |
| Balance calls for many shared wallets slow the list | Cards never wait for balances; calls run in parallel and each fills independently. |
| People miss that the menu changed | Name tab on the bar plus context line; SC-002 moderated check before release. |

## Complexity Tracking

No constitution violations to justify.
