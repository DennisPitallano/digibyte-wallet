# Research: First-Receive Flow

Findings from reading the code the feature touches. Each entry is a decision the plan relies on.

## R1. Where the "first payment" rule lives

- **Decision**: A pure static rule, `FirstReceiveRule`, in `src/DigiByte.Wallet/Services/`, with a small store, `FirstReceiveStore`, beside it. The web app calls them through one scoped service, `FirstReceiveCoordinator`, in `src/DigiByte.Web/Services/`.
- **Rationale**: The rule decides whether a wallet "started empty" and whether a load contains a first payment. It is wallet logic with several negative paths (failed load, history already present, non-payment first transaction), and the only test project for this code is `tests/DigiByte.Wallet.Tests` (there is no web test project). Constitution III asks for tests first on wallet logic; putting the rule in the library is what makes that possible. Precedent: `HotAddressSelector`, `AttentionCounter`, `PresenceDebouncer`.
- **Alternatives considered**: Logic inside each page (three copies, untestable). A web-only service (no test project to cover it).

## R2. How "created on this device" is known (FR-011)

- **Decision**: `CreateWallet.razor` calls `FirstReceiveCoordinator.MarkCreatedEmptyAsync(wallet.Id)` right after `WalletService.CreateWalletAsync` returns. `WalletService.CreateWalletAsync` is not changed.
- **Rationale**: `CreateWalletAsync` is called by both `CreateWallet.razor` (a newly generated phrase) and `RecoverWallet.razor` (an existing phrase), so the service cannot tell the two apart; only the calling page can. Marking from the page keeps creation code untouched (FR-034).
- **Alternatives considered**: A new parameter on `CreateWalletAsync` (touches the creation path and its callers for a non-secret UI record).

## R3. One record per wallet, not two

- **Decision**: One stored value per wallet, key `firstReceive:{walletId}`, with three values: `empty`, `history`, `celebrated`. No key means "not yet decided". The spec's "first-load record" and "celebration record" are the states of this one value.
- **Rationale**: The two records are never independent: only an `empty` wallet can become `celebrated`. One value removes impossible combinations and is one key to delete with the wallet (FR-028).
- **Alternatives considered**: Two keys (allows `history` + `celebrated`, which has no meaning).

## R4. What counts as a complete load (FR-015)

- **Decision**: Add `WalletService.LoadTransactionHistoryAsync`, which returns the transactions together with two flags for that call: `Complete` (the chain was read) and `AddressesSettled` (the wallet has finished finding its used addresses). A load counts as complete only from that result. A saved snapshot (`WalletSnapshotCache`) may only move a wallet from undecided to `history`; it can never record `empty` and never triggers the celebration.
- **Rationale**: Found in analysis. The existing `LastHistoryIncomplete` is one property on the service, reset to false at the start of every history call, so with two overlapping calls a failed load can be read as complete. This feature adds a second caller on Receive (the automatic check beside the page's own load), which makes overlap likely, and a wrong "complete and empty" would record a recovered wallet as started-empty and celebrate later. Separately, a history call waits at most 5 seconds for address discovery and then works from saved indexes, so an early load of a recovered wallet can be empty while the chain read succeeded; `AddressesSettled` stops that load from proving "empty". A snapshot with transactions proves the wallet has history; a snapshot without them proves nothing.
- **Alternatives considered**: Reading `LastHistoryIncomplete` straight after the call, as Activity and Receive do today (wrong once calls overlap). Requiring settled addresses for the celebration too (a discovery outage would then delay a new user's celebration for no benefit, since a wallet created here is already known to be empty).

## R5. Detecting the first payment, including one that arrived while closed

- **Decision**: For a wallet in state `empty`, any complete load that contains at least one transaction with `Direction == Received` and a positive DGB amount is the first payment, whatever its age. The celebration total is the sum of all such transactions in that load. If the load has transactions but none is a DGB receipt, the wallet moves to `history` with no celebration.
- **Rationale**: Home's existing "payment received" toast ignores transactions older than 15 minutes with more than 2 confirmations, which is right for a toast but would miss a payment that arrived overnight. The celebration is driven by the stored state, not by recency.

## R6. Where the celebration is drawn, and "wait for other dialogs" (FR-013, FR-023)

- **Decision**: A component, `FirstReceiveCelebration.razor`, placed on Home, Receive and Activity. Each page passes `Blocked="true"` while one of its own dialogs is open; `MainLayout` reports the "What's New" window to the coordinator. The coordinator holds the pending celebration in memory; the wallet's state changes to `celebrated` at the moment the overlay is actually shown.
- **Rationale**: Dialogs in this app are page-local booleans, with no global registry, so only the page knows whether it has one open. Marking `celebrated` on show (not on detection) means a celebration that could not be shown is offered again on the next page that loads history, and one that was shown is never repeated, including when the wallet locks while it is up.
- **Alternatives considered**: A single layout-level overlay (cannot see page-local dialogs).

## R7. One "payment received" notice across pages (FR-022, FR-026)

- **Decision**: Move Home's arrival rule (`AnnounceNewReceivedAsync`, which tracks known transaction IDs) into a pure `NewArrivals` rule in the wallet library plus a small `IncomingPaymentAnnouncer` in the web app. Each page that checks (Home, Receive) owns its own announcer, whose list of seen IDs starts empty when the page opens and is cleared on a wallet switch. It shows the existing toast unless the coordinator just returned a celebration for the same load.
- **Rationale**: This is exactly the lifetime of Home's `knownTxIds` today: it is reset on every wallet switch and is lost whenever Home is re-created (navigation, unlock), and the first list after that is recorded without announcing. Because every page starts silent, a payment announced on one page is not announced again on the next, so "once per payment" holds without shared state.
- **Alternatives considered**: One list per wallet kept for the whole app session (first draft of this plan). Found in analysis to change behaviour: a payment that arrived while the wallet was locked, another wallet was active, or the user was on another page would be announced on return. That may be worth doing, but it is a visible change outside this feature's scope.

## R8. Receive's automatic check (FR-024, FR-025)

- **Decision**: Extract Home's poller pacing (15-second base tick, skip while the tab is hidden, back off 15 s → 30 s → 60 s while nothing changes, reset on change) into a pure `RefreshPacer` in the wallet library and a small `AutoRefreshLoop` in the web app. Home is switched to it with no behaviour change; Receive starts one on open and stops it on leave. Receive's tick calls `WalletService.GetTransactionHistoryAsync` (the tiered refresh from spec 024), ignores a failed or incomplete answer, and passes a complete one to the coordinator and the announcer.
- **Rationale**: "Same pacing as Home" is only true if both use the same code. The history call already goes through the tiered refresh, so a quiet check costs about 10 requests, and identical reads made at the same moment share one request (spec 023).
- **Alternatives considered**: Copying the timer into Receive (two pacings to keep in step). A shared background service that runs on every page (adds traffic on pages that show no balance, which the spec does not ask for).

## R9. The first-session marker (FR-029)

- **Decision**: Device key `firstSession:ended` = `1`. The coordinator loads it once at startup. If it is absent, the coordinator looks at each wallet's saved snapshot once; any snapshot with transactions sets the marker. After that, any observed history (saved or network) with a transaction sets it. It is never cleared.
- **Rationale**: The snapshot pass means an existing user sees the banner and sponsors strip as before from the first page after updating, whichever page they open. "Unknown" is treated as "not ended", so nothing appears and then disappears (Story 4, scenario 3).

## R10. Hiding the install banner and sponsors strip

- **Decision**: `PwaInstallBanner` (drawn by `MainLayout` on every page) shows only when its existing rules pass **and** (the page is one of its always-show setup pages **or** the first session has ended). Home wraps `SponsorsStrip` in the same condition. Both re-render on the coordinator's change event.
- **Rationale**: The banner already has an always-show list (`/welcome`, `/recover-wallet`, `/import-xpub`), which is exactly the set FR-030 leaves alone.

## R11. The Install app action in the celebration (FR-027)

- **Decision**: Move `InstallMode`, `InstallState` and `ComputeInstallMode` from `Settings.razor` into a shared `InstallAppState` helper in `src/DigiByte.Web/Services/`. The celebration shows the action when the mode is not `Hidden`. In `Native` mode it calls `pwaInstall.prompt` directly; in the two steps modes it navigates to `/settings?install=1`, and Settings opens its existing steps dialog for that query.
- **Rationale**: One rule decides "is the wallet installed" for both places (spec assumption). Reusing Settings' dialog avoids a second copy of the iOS and menu instructions.
- **Alternatives considered**: Extracting the steps dialog into a shared component (a larger refactor of spec 019 for the same result).

## R12. Empty state on Home and Activity (FR-003 to FR-009, FR-010)

- **Decision**: A new `ReceiveEmptyState.razor` component for Home: QR code (QRCoder, the same generator Home's QR dialog uses), the address in full, **Copy address**, and a **Receive** link. It takes the address Home already holds (`receivingAddress`, from `WalletService.GetReceivingAddressAsync`), so it matches the balance card and the Receive page. Home shows it when history is known to be empty and keeps today's "Unable to load data" card when `fetchFailed`. Activity adds a **Receive** button to its "No transactions yet" branch only when `all.Count == 0` and the load did not fail.
- **Rationale**: The address is derived locally, so the component works offline (FR-007). Activity's existing branch already distinguishes "no transactions" from "nothing matches" by `all.Count`.

## R13. The mascot message (FR-005, FR-032)

- **Decision**: Reword the existing `home.empty` entry in `BakuCatalog` (title and body) to point at the address and QR code. English only. No change to mascot rules.
- **Rationale**: The message is a dismissible tip that `BakuService` hides when the mascot is Off or Guardian only; the catalogue is plain C# text and does not use the translation files. A user who already dismissed `home.empty` will not see the new wording, which FR-005 allows.

## R14. Removing the introduction slides (FR-001)

- **Decision**: Delete `Components/Shared/OnboardingWalkthrough.razor` and its one use in `Home.razor`; remove its mention in `docs/ARCHITECTURE.md`. The `dgb-onboarding-done` browser key is left alone.
- **Rationale**: Home is the only place that renders it.

## R15. Translations (FR-032)

- **Decision**: New strings go in `wwwroot/locales/en.json` and `es.json` under `firstreceive.*`, `home.empty.*` and `activity.empty.*`, read with `LocalizationService.T` in the new components. Home and Activity themselves do not use the translation service today; only the new components inject it.

## R16. Hidden balances (FR-017)

- **Decision**: The celebration reads `pref:hideBalance` from `ISecureStorage`, the same key Home writes and Activity reads.

## Found during implementation

These were not known when the plan was written. Each changed the code, and the contracts below were updated to match.

- **A failed summary read was reported as a complete, empty history.** When no explorer can answer an address summary, the fallback raises the same exception it uses for "summaries do not exist on this route", and `WalletService` then reads each address in full. That reader (`BlockchainApiService.GetAddressTransactionsAsync`) returns an empty list for an address it cannot reach. With the explorer unreachable, the load therefore came back empty and "complete". `LoadTransactionHistoryAsync` now reports the load as incomplete when the summary failure has a cause attached; without one (regtest, the gateway answering first) the per-address read is still the normal, complete path. Known limit: with the developer switch for change-only sync turned off there is no summary read to fail, so an unreachable explorer still reads as empty in that mode.
- **Home's notice starts from the saved history, not from the first network list.** `LoadFromCacheAsync` seeds the seen list with the saved transactions, so a recent payment that is not in the saved list is announced when Home opens. `IncomingPaymentAnnouncer.Seed` keeps that. So that a payment announced on Receive is not announced again on Home, Receive saves the transactions of a complete load, as Activity already does.
- **Home's pacing is 15 s, 30 s, 30 s, then 60 s.** One or two unchanged checks give a 30-second interval; three or more give 60. `RefreshPacer` and its tests follow the code, not the plan's shorthand.
- **Home hid the address until its network fetch finished, and could not scroll.** The receiving address was assigned after the first fetch, so an offline or slow first load showed no address; it is now assigned before the fetch. Home also turned page scrolling off unconditionally so its list could scroll; it now does so only while the list exists.
- **FR-007 and FR-008 conflicted for a new wallet that is offline.** One asked for the address with no network, the other for "Unable to load data" instead of the empty state. Home now shows the failure card with the address card beneath it, and FR-008 says so.
- **A refused clipboard write must not take the page down.** `ClipboardService.CopyAsync` throws when the browser refuses, by contract. `ReceiveEmptyState` catches that and shows a notice (`home.empty.copy_failed`).
- **The loop and the announcer are created by the page, not injected.** A transient disposable resolved from the app's single scope is kept alive until the app closes, so each page constructs its own and stops it on leaving.
- **The celebration's mascot line is drawn by the celebration itself.** The mascot service shows only the highest-priority message on screen, and Receive already has a tip that would outrank it. The line is shown when the mascot is fully on, which is the same rule.
- **The wallet test project references the web project.** So `FirstReceiveCoordinator` has unit tests too, although the plan said web code could only be checked by hand.
- **The animation test page can preview the celebration** (`/test-animations`), because the explorers cannot be reached from a local preview and a real first payment cannot be produced there.

## Open items

None. Nothing in the Technical Context needs clarification.
