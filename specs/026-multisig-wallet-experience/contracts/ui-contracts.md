# UI Contracts: Multisig Wallet Experience

The app exposes no new API. Its contracts are routes, the menu, and the reusable components. Requirement ids refer to [spec.md](../spec.md).

## Routes

| Route | Page | Menu | Change |
|---|---|---|---|
| `/multisig` | `MultisigWallets` | Personal (More current) | Redesigned list |
| `/multisig/{id}` | `MultisigDetail` | Shared, Overview current | Context line, two action tiles, records last-used |
| `/multisig/{id}/send?address=&amount=` | `MultisigSend` | Shared, none current | Accepts `address`, `amount` (new, optional) |
| `/multisig/{id}/pending` | `MultisigPending` | Shared, Pending current | Grouped list only |
| `/multisig/{id}/pending/{pendingId}` | `MultisigPendingDetail` (new) | Shared, Pending current | All actions on one payment |
| `/multisig/{id}/activity` | `MultisigActivity` | Shared, Activity current | Context line |
| `/multisig/sign-room?code=` | `MultisigSigningRoom` | Personal on join; hidden in room and on result | Redesigned |
| `/multisig/create`, `/multisig/import`, `/multisig/room`, `/help/multisig` | unchanged | Personal | none |

Query values allowed in an address: `address`, `amount`, `code`, `scanned`, `draft`. Never a proposal, a redeem script or a key.

## Bottom menu (`NavMenu`)

| Slot | Personal | Shared |
|---|---|---|
| 1 | Wallet → `/` | Overview → `/multisig/{id}` |
| 2 | Activity → `/transactions` | Activity → `/multisig/{id}/activity` |
| 3 | Scan (button) | Scan (button), payment codes route to `/multisig/{id}/send` (FR-002a) |
| 4 | Request → `/payments` | Pending → `/multisig/{id}/pending`, count = NeedsYou for this shared wallet (a payment known to be unable to go through is not counted) |
| 5 | More (button), count = NeedsYou total | More (button), no count |

- Shared mode adds the name tab above the bar: key icon, "*name* · *M-of-N*", opens the switcher. `aria-label` of the `nav`: "*name* shared wallet navigation".
- Counts: hidden at 0, "99+" above 99, `aria-label` such as "2 multisig payments need you".
- Test ids: `nav-shared-tab`, `nav-overview`, `nav-pending`, `nav-pending-count`, `nav-more-count` (existing `nav-scan`, `nav-more` kept).
- Multisig row in the More list shows the same total count.

## Components (`DigiByte.Web/Components/Multisig`)

| Component | Parameters | Behaviour |
|---|---|---|
| `SharedWalletContextLine` | `Config`, `OnSwitch` | Name, scheme chip, "in *wallet*" with colour dot. One button; opens the switcher. Names truncate after at least 12 characters. Replaces `WalletChip` on multisig pages. |
| `SharedWalletSwitcher` | `IsOpen`, `CurrentId`, `OnClosed` | Sheet: "Back to *wallet*" first, then the wallet's other shared wallets with scheme and count, then "All shared wallets". Focus trapped, Esc closes. |
| `MultisigWalletCard` | `Model` (WalletCardModel), `HideBalance`, `OnOpen`, `OnRename` | Whole card opens the wallet. Status line, "Not backed up", "View only" with icon and text. Balance area has a fixed height. Rename under a "more" button. |
| `PendingCard` | `Pending`, `Config`, `State`, `Action` (PendingCardAction), `CoinsSpent`, `OnOpen`, `OnAction` | One status, amount, short recipient, signer progress, age, one button. Tapping the card body raises `OnOpen`. No irreversible control. |
| `AddProposalSheet` | `IsOpen`, `CanJoinRoom`, `OnScan`, `OnPaste`, `OnFile`, `OnJoinRoom`, `OnClosed` | Four labelled choices with one-line explanations. |
| `StickyAction` | `Label`, `BusyLabel`, `Busy`, `DisabledReason`, `Tone`, `OnAction`, optional `ChildContent` (for `SlideToConfirm`) | Pinned to the bottom edge above the system bar. Reserves space so content is not covered. |
| `RoomBar` | `InviteCode`, `Countdown`, `IsReconnecting`, `OnMenu` | Code, time left (emphasised when urgent), connection state, menu button. Sticky at the top of the room. |
| `RoomStageTracker` | `Stage` | Invite · Sign · Broadcast with the current step marked; text alternative "Step 2 of 3: Sign". |
| `RoomInvite` | `InviteCode`, `InviteLink`, `Prominent`, `OnShare`, `OnCopyLink`, `OnCopyCode` | Prominent: code, Share, Copy link, Copy code, QR. Folded: one row with the code and a control to expand. Share uses the device share sheet when available, else copies the link. |
| `RoomMenu` | `IsOpen`, `IsHost`, `OnLeave`, `OnClose`, `OnClosed` | Leave and Close room, each with its consequence; Close room confirms. |
| `RoomCodeInput` | `Value`, `ValueChanged`, `OnScan`, `OnSubmit` | Six characters, uppercase. Accepts a full paste, including an invite link (the code after its `code=` is taken). No length limit of its own: `Clean` decides what is kept and the field is always set back to that, so a stray character never uses up a box. Exposes one labelled input to assistive technology. |
| `NeedsYouCount` | `Class`, `TestId`, `Spoken` | The shared count as a bubble; shows nothing at zero and follows changes by itself. With `Spoken` it also says the count in words from inside the control it sits on. |

Existing components reused unchanged: `PaymentSummary`, `SignerList`, `SignerCard`, `SignerProgress`, `StatusBadge`, `RoomHeadlineCard` (its button is not rendered when `StickyAction` shows it), `ConnectionBanner`, `QrImage`, `SlideToConfirm`, `RenameDialog`, `EmptyState`, `AttentionBadge`.

## Services

| Service | Project | Contract |
|---|---|---|
| `ShellContext.Resolve(string path)` | Web/Layout | Pure. See data model. |
| `ShellChrome` | Web/Services (scoped) | `bool HideNav { get; }`, `void SetHideNav(bool)`, `event Action Changed` |
| `MultisigAttentionState` | Web/Services (scoped) | `AttentionSummary Current`, `Task RefreshAsync()`, `event Action Changed` |
| `ScanRouter.Route(scanned, appBaseUri, sharedWalletId = null)` | Web/Services | Payment → shared send address when `sharedWalletId` is set; all other kinds unchanged |
| `MultisigWalletService.PendingChanged` | Wallet | `event Action<string>` (shared wallet id) |
| `AttentionCounter`, `PendingGrouping`, `PendingCardActions`, `WalletCardStatus`, `SharedWalletOrder`, `SharedWalletShortcut`, `RoomStage` (stage and `InviteProminent`), `SignerOrder`, `RoomCountdown`, `CountBadge` | Wallet | Pure functions; see data model and research. |

## Pending groups

| Group | Heading | Shown |
|---|---|---|
| NeedsYou | Needs you | Open; counted |
| WaitingForOthers | Waiting for others | Open |
| CantGoThrough | Can't go through | Open; coins known to be spent; not counted; card button "Review & cancel" |
| Cancelled | Cancelled | Folded by default |

`MultisigAttentionState.ReportSpent(checkedIds, spentIds)` is how the Pending list and the payment's own screen tell the counts which payments cannot go through; `IsSpent(id)` is read by the list of shared wallets. Kept in memory for the session only.

## Behaviour guarantees

- No page other than `MultisigPendingDetail` and `MultisigSigningRoom` can sign, send or retry (FR-019a).
- Every existing handler on `MultisigPending` keeps its checks when moved (double-submit guards, spent-coin check, confirm steps).
- Nothing new is sent to the signing service or any backend; the list makes the same balance and price calls the dashboards make.
