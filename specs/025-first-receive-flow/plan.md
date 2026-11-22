# Implementation Plan: First-Receive Flow

**Branch**: `feature/first-receive-flow` (spec directory `025-first-receive-flow`) | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/025-first-receive-flow/spec.md`

## Summary

Make a new user's first received payment the centre of the first session.

1. **Empty Home and Activity**: remove the five introduction slides; a new `ReceiveEmptyState` component puts the QR code, address, **Copy address** and a **Receive** link where Home's list will be; Activity's empty state gains a **Receive** button; the mascot's `home.empty` message is reworded.
2. **First-payment celebration**: a pure `FirstReceiveRule` with one stored state per wallet (`empty`, `history`, `celebrated`) decides whether a history load contains a wallet's first payment. A `FirstReceiveCoordinator` in the web app is called by Home, Receive and Activity after every history load, and a `FirstReceiveCelebration` overlay on those pages shows it once, with an **Install app** action.
3. **Receive checks by itself**: Home's poller pacing is extracted into a shared `RefreshPacer` and `AutoRefreshLoop`; Receive runs one while open. Home's "payment received" rule moves into a small announcer class that Home and Receive each own, with the same lifetime Home's list has today, so a payment is announced once and nothing else about the notice changes.
4. **Quieter first session**: a per-device marker hides the install banner on pages inside the wallet, and the sponsors strip on Home, until some wallet has a transaction.

No server, signing, secret-storage or wallet-creation change. The only added traffic is Receive's check, which makes the requests Home's refresh already makes.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA)

**Primary Dependencies**: Existing only (QRCoder for the QR code, already used by Home and Receive); no new packages

**Storage**: `ISecureStorage`. Two new non-secret keys: `firstReceive:{walletId}` (per wallet) and `firstSession:ended` (per device). Pending celebration, known arrivals and pacing are in memory only. See [data-model.md](data-model.md).

**Testing**: xUnit in `tests/DigiByte.Wallet.Tests` for the rule, store, arrivals rule, pacer, per-call history completeness and wallet deletion, written first. There is no web test project, so pages and components are checked in the browser preview with [quickstart.md](quickstart.md).

**Target Platform**: Installed PWA and mobile/desktop browsers

**Project Type**: Web app (Blazor WASM) with a separate wallet-logic library

**Performance Goals**: SC-005 celebration on screen within 2 s of the page showing the payment; SC-008 a payment shows on an open Receive page within one check interval (15 s when active); Receive open for 5 minutes costs about what Home open for 5 minutes costs.

**Constraints**: Works offline for the empty state; a failed or partial load never decides anything; nothing new leaves the device; no new kind of request or destination; reduced motion and mascot setting honoured; the celebration never covers another dialog.

**Scale/Scope**: 3 new pure classes and 1 store in the wallet library, plus 1 new method on the wallet service; 4 new web services; 2 new components, 1 removed; changes to 7 pages or layout components; about 12 new translation keys in 2 languages.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1. Still passes.*

| Principle | Assessment |
|-----------|------------|
| I. Self-custody | No key, seed or credential is read, stored or sent. The two new stored values are a three-value state and a flag. Pass. |
| II. In-browser cryptography | No signing or encryption path is touched. `CreateWalletAsync` is unchanged; the "created empty" mark is written by the page afterwards. Pass. |
| III. Test-first (wallet logic) | `FirstReceiveRule`, `FirstReceiveStore`, `NewArrivals`, `RefreshPacer`, `LoadTransactionHistoryAsync` and the `DeleteWalletAsync` change live in `DigiByte.Wallet` with tests written first, including negative paths: incomplete load, overlapping loads, addresses not yet settled, snapshot without transactions, history already present, first transaction not a receipt, unreadable stored value, a state that must not move backwards. Pass. |
| IV. Boundary discipline | Decisions in `DigiByte.Wallet`; `DigiByte.Web` owns timers, dialogs, toasts and install. `DigiByte.Web` → `DigiByte.Wallet` only, as today. Pass. |
| V. Resilience | The empty state needs no network. A failed check on Receive changes nothing on screen and is retried. Unknown first-session state hides the optional items, never core flows. Balance, receive, send and history do not depend on any new piece. Pass. |
| Security & privacy | No third-party script, no CSP change, no new origin. Receive's check asks the same data sources the same questions Home's refresh does, so no new address-to-client link is revealed beyond what an open Home already reveals. Nothing about the celebration is sent anywhere. Pass. |

No violations, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/025-first-receive-flow/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── first-receive.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/Services/
├── FirstReceiveRule.cs            # NEW: pure state rule (FR-011 to FR-015)
├── FirstReceiveStore.cs           # NEW: firstReceive:{walletId}, firstSession:ended (FR-014, FR-028, FR-029)
├── NewArrivals.cs                 # NEW: Home's arrival rule, moved (FR-022, FR-026)
├── RefreshPacer.cs                # NEW: Home's poller pacing, moved (FR-024)
└── WalletService.cs               # LoadTransactionHistoryAsync (per-call completeness); DeleteWalletAsync also removes the first-receive key

src/DigiByte.Web/
├── Program.cs                     # register the four new services
├── Services/
│   ├── FirstReceiveCoordinator.cs     # NEW: observe, pending, shown, first-session marker
│   ├── IncomingPaymentAnnouncer.cs    # NEW: Home's "payment received" notice, one instance per page
│   ├── AutoRefreshLoop.cs             # NEW: timer + visibility pause around RefreshPacer
│   ├── InstallAppState.cs             # NEW: install mode rule, moved from Settings
│   └── BakuCatalog.cs                 # reword home.empty
├── Components/
│   ├── Wallet/ReceiveEmptyState.razor         # NEW (FR-003 to FR-007)
│   ├── Shared/FirstReceiveCelebration.razor   # NEW (FR-016 to FR-023, FR-027)
│   ├── Shared/OnboardingWalkthrough.razor     # DELETED (FR-001)
│   └── PwaInstallBanner.razor                 # first-session condition (FR-029, FR-030)
├── Layout/MainLayout.razor        # InitializeAsync; report "What's New" as an open dialog
├── Pages/
│   ├── CreateWallet.razor         # MarkCreatedEmptyAsync after create (FR-011)
│   ├── Home.razor                 # empty state, observe, announcer, AutoRefreshLoop, sponsors condition, celebration; no walkthrough
│   ├── Receive.razor              # observe, announcer, AutoRefreshLoop, celebration (FR-024 to FR-026)
│   ├── Transactions.razor         # observe, Receive button in empty state, celebration (FR-010)
│   └── Settings.razor             # use InstallAppState; open steps for ?install=1
└── wwwroot/
    ├── locales/en.json, es.json   # firstreceive.*, home.empty.*, activity.empty.*
    └── css/app.css                # celebration styles, reduced-motion rule (reusing the send overlay's)

tests/DigiByte.Wallet.Tests/
├── FirstReceiveRuleTests.cs       # NEW
├── FirstReceiveStoreTests.cs      # NEW
├── NewArrivalsTests.cs            # NEW
├── RefreshPacerTests.cs           # NEW
├── HistoryLoadTests.cs            # NEW: overlapping loads, addresses not settled
└── MultiWalletTests.cs            # (existing) + deleting a wallet removes its first-receive key, keeps the marker

docs/ARCHITECTURE.md               # remove the walkthrough; add the first-receive flow
CHANGELOG.md                       # Unreleased: Added / Changed / Removed
```

**Structure Decision**: The existing two-layer layout. Rules and storage go in `src/DigiByte.Wallet/Services` so they can be tested in `tests/DigiByte.Wallet.Tests`; timers, dialogs and pages stay in `src/DigiByte.Web`. No new project.

## Design notes

Decisions and their reasons are in [research.md](research.md); this is the order of work they imply.

1. **Wallet library first, tests first**: rule, store, arrivals, pacer, deletion. Nothing visible changes.
2. **Refactors with no behaviour change**: Home onto `AutoRefreshLoop` and `IncomingPaymentAnnouncer`; Settings onto `InstallAppState`. Verified by Home refreshing and announcing as before, and Settings' Install app entry working as before.
3. **Story 1**: delete the walkthrough, add `ReceiveEmptyState`, reword the mascot message, add Activity's button. Shippable on its own.
4. **Stories 2 and 3**: coordinator, celebration component, observe calls on the three pages, the create mark, the install action.
5. **Receive's check**: `AutoRefreshLoop` on Receive with observe and announce.
6. **Story 4**: first-session marker in the banner and around the sponsors strip.
7. **Text and docs**: translations, changelog, architecture note.

### Risks

| Risk | Handling |
|------|----------|
| Refactoring Home's poller changes its behaviour | Step 2 is a pure move covered by `RefreshPacerTests`; checked in the preview before anything else builds on it. |
| A failed or early load is taken as "complete and empty" | Completeness and settled addresses are returned per call by `LoadTransactionHistoryAsync` (research R4), with a test for overlapping loads; pages never read the shared flag. |
| Two pages detect the first payment at once | The coordinator serialises observe calls per wallet, and the state only changes to `celebrated` on show. |
| A celebration is detected but the user leaves before it can show | The state stays `empty`, so the next complete load on Home, Receive or Activity offers it again. |
| Receive's check adds load on the explorers | Same pacer as Home (pause when hidden, back off to 60 s) on top of the tiered refresh; step 3 of the quickstart compares the two. |
| Existing users lose the banner after updating | The startup snapshot pass sets the marker for any device with a funded wallet. |

## Complexity Tracking

No constitution violations to justify.
