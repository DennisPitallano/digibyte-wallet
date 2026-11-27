# Implementation Plan: Requests That Know When They're Paid

**Branch**: `feature/paid-aware-requests` (spec directory `036-paid-aware-requests`) | **Date**: 2026-11-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/036-paid-aware-requests/spec.md`

## Summary

Turn the Request tab from a QR code maker into something that knows when it has been paid.

1. **Each request gets its own address, and Receive's address doesn't move.**
   - One new field, `WalletInfo.HandedOutTop`, records the highest receive index handed out to anyone.
   - `ReceiveScanSet` counts it, so Receive and requests never share an index and share the existing budget of 20 unpaid addresses. FR-005's refusal is the existing `TooManyUnused` failure.
   - `WalletService.HandOutRequestAddressAsync` mirrors `GenerateNewReceivingAddressAsync` but never touches the address on show. Spec 027's rules stay intact, so a restore from seed still finds every request payment.
2. **Status worked out from scratch.**
   - A pure `RequestStatusCalculator` turns a request plus the current transactions paying its address into its status. The statuses are Waiting, Payment arriving, Partly paid (with a "+… arriving" note), Paid, Overpaid, Expired, Partly paid · expired and Cancelled.
   - It also records each payment's first-seen time, for late flags.
   - Status is never accumulated, so a dropped transaction simply stops counting.
3. **An app-wide watcher.**
   - `RequestWatcher` checks live requests by their stored address string through the existing explorer service: every 3 s for the request on screen, every 30 s otherwise, and every 60 s while hidden.
   - It needs no unlock, so it keeps working while the wallet is locked or in the background.
   - It raises "arriving" and "paid" events once per request. The page shows a full-screen card with a vibration. Elsewhere in the wallet it shows a tappable toast; in the background, a system notification (browser Notification API, no push server); on the lock screen, a notice card.
   - After a start, unlock or wallet switch, one "N requests paid since you were last here" message, and a count on the Request tab.
4. **Requests in your own currency.**
   - A small `PriceQuoteService` returns the price and when it was fetched, without throwing.
   - A currency request fixes its DGB amount at that price for 15 minutes.
   - Its link carries `fiat`, `currency` and `expires` (other wallets ignore them). Send and the web payment page show "Price held until…" and warn after it has passed.
5. **Keypad first.** A new keypad component, remembering DGB or the person's currency. Label and message sit behind "Add a note". There is a filtered list with cancel and remove.
6. **Storage.**
   - Requests move from one list shared by every wallet to one list per wallet. Old requests show "Not tracked".
   - The request's label is written as the address label, so Activity, the detail view and CSV export show it with no new code.

There is no server change, no new network destination, no new package and no CSP change. The added traffic is per-address explorer lookups for live requests, which go to the explorer the wallet already uses.

## Technical Context

**Language/Version**: C# / .NET 10, Blazor WebAssembly (PWA); a little JavaScript in `wwwroot/js`

**Primary Dependencies**: Existing only (NBitcoin, QRCoder). Browser Notification API and `navigator.vibrate`, through new and existing JS helpers.

**Storage**:
- `payment_requests:{walletId}` (was `payment_requests`) in `ISecureStorage`, with new fields on `PaymentRequest`.
- One new field on `WalletInfo` (`HandedOutTop`).
- Three localStorage keys.
- The address label through the existing `AddressLabelService`.
- See [data-model.md](data-model.md).

**Testing**:
- xUnit in `tests/DigiByte.Wallet.Tests` (references DigiByte.Web), written first, using the existing `Env` / `FakeChain` and history-sync test doubles.
- There is no bUnit or Playwright for wallet pages, so all decisions live in pure classes. Pages are checked in the browser preview with [quickstart.md](quickstart.md).

**Target Platform**: Installed PWA and mobile and desktop browsers. Notifications and vibration where the browser supports them, degrading silently where it doesn't.

**Project Type**: Web app (Blazor WASM) over a wallet-logic library

**Performance Goals**:
- SC-002: arriving and paid shown within 5 s of the explorer reporting them, because the request on screen is checked every 3 s.
- SC-003: QR code in under 10 s and 4 taps.
- No extra lookups for a wallet with no live requests.

**Constraints**:
- No unlock is needed to watch.
- Paid is announced exactly once per request.
- Receive's address never moves because of a request.
- Requests and Receive share the 20-unpaid budget.
- A broken price-lock field never invalidates a link.
- Nothing about a request reaches a project server.
- English and Spanish.

**Scale/Scope**:
- Wallet library: 5 new pure classes (`RequestStatusCalculator`, `RequestAnnouncementRule`, `RequestAmounts`, `RequestKeypadEntry`, `RequestMigration`) and 2 changed models (`PaymentRequest`, `WalletInfo`).
- Changes to `ReceiveScanSet`, `WalletService`, `PaymentRequestService`, `PaymentLink`; in Web, `ScanRouter`, `ScanHandOff`, `PaymentHandoff`.
- Web: 4 new services (`RequestWatcher`, `RequestAnnouncer`, `PriceQuoteService`, `RequestAttentionState`) and 3 new components (`RequestKeypad`, `RequestPaidOverlay`, `RequestStatusPill`), plus 1 new JS file.
- Changed pages: `Payments`, `Send`, `Pay`, `Unlock`, `Settings`, `NavMenu`, plus `NotificationService` and `ToastContainer`.
- About 60 translation keys in 2 languages. `docs/PROCESS_FLOWS.md` updated.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1. Still passes.*

| Principle | Assessment |
|-----------|------------|
| I. Self-custody | No seed, key or password is read, stored or sent. Request addresses are derived by the existing HD path inside the unlocked session. The watcher uses only stored public addresses. Pass. |
| II. In-browser cryptography | No signing change. Notifications are shown by the browser from the page or service worker. There is no push service, and no server is involved (FR-014). Pass. |
| III. Test-first (wallet logic) | `ReceiveScanSet`, `HandOutRequestAddressAsync`, `RequestStatusCalculator`, `RequestAmounts`, `RequestKeypadEntry`, the migration, `PaymentLink` / `PaymentRequest` and `PaymentRequestService` are in `DigiByte.Wallet`, each with tests written first. The negative paths covered: gap limit reached, save failure undo, single-key wallet, malformed and unknown-currency link fields, a dropped transaction, a clock moved backwards and forwards, a price fetch that fails, migration of unreadable JSON. Pass. |
| IV. Boundary discipline | Rules and storage in `DigiByte.Wallet`; the watcher, notifications, price wrapper and pages in `DigiByte.Web`. The dependency direction is unchanged. DigiPay (`Pay.Web`) is not touched; the POS keypad's rules are mirrored, not moved (research R7). Pass. |
| V. Resilience | A failed lookup keeps the last status and marks it "not up to date". A failed price fetch disables only the currency option. Making DGB requests and the balance never depend on the watcher or the price. The balance includes request addresses through the existing scan set, whatever the watcher does. Pass. |
| Security & privacy | No CSP change, no new origin, no third-party script. Price-lock fields stay after the `#`. Notifications hide the amount and label unless the person turns them on, and never show the address. Address lookups go to the explorer already used for the balance. Pass. |

No violations, so Complexity Tracking is empty.

**Pre-existing issue found during research (outside this feature):**
- Auto-lock only navigates to `/unlock`. It never calls `WalletService.LockWalletAsync`, so the decrypted wallet session stays in memory while the screen says the wallet is locked.
- This feature neither depends on that nor changes it (research R3).
- It should be raised as its own security review.

## Project Structure

### Documentation (this feature)

```text
specs/036-paid-aware-requests/
├── spec.md
├── plan.md                       # This file
├── research.md                   # Phase 0
├── data-model.md                 # Phase 1
├── quickstart.md                 # Phase 1
├── contracts/
│   ├── payment-link-fields.md    # Phase 1: price-lock keys in request links
│   └── request-watcher.md        # Phase 1: hand-out, status, watcher, notifications
├── checklists/requirements.md
└── tasks.md                      # Phase 2 (/speckit-tasks; not created here)
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Models/
│   ├── PaymentRequest.cs            # new fields, statuses, price-lock keys in ToUri/FromUri
│   ├── RequestPayment.cs            # new
│   └── WalletInfo.cs                # + HandedOutTop
└── Services/
    ├── ReceiveScanSet.cs            # counts HandedOutTop
    ├── WalletService.cs             # HandOutRequestAddressAsync, RecordHandOut, SetExtraHotAddresses, TransactionsChanged
    ├── PaymentRequestService.cs     # per-wallet storage, create/renew/cancel/remove/apply
    ├── RequestStatusCalculator.cs   # new, pure
    ├── RequestAnnouncementRule.cs   # new, pure (which way a paid request is announced)
    ├── RequestAmounts.cs            # new, pure (currency → DGB)
    ├── RequestKeypadEntry.cs        # new, pure
    ├── RequestMigration.cs          # new, pure
    └── PaymentLink.cs               # price-lock fields

src/DigiByte.Web/
├── Services/
│   ├── RequestWatcher.cs            # new, app-wide
│   ├── RequestAnnouncer.cs          # new, app-wide (acts on RequestAnnouncementRule)
│   ├── RequestAttentionState.cs     # new (tab count)
│   ├── PriceQuoteService.cs         # new
│   ├── NotificationService.cs       # + tap action
│   └── ScanRouter.cs, ScanHandOff.cs, PaymentHandoff.cs  # carry the fields
├── Components/Shared/
│   ├── RequestKeypad.razor          # new
│   ├── RequestPaidOverlay.razor     # new
│   ├── RequestStatusPill.razor      # new
│   └── ToastContainer.razor         # tap action
├── Pages/
│   ├── Payments.razor               # keypad first, list, detail, filters
│   ├── Send.razor                   # price-lock line and warning
│   ├── Pay.razor                    # price-lock line on the no-wallet card
│   ├── Unlock.razor                 # "Request paid" notice
│   └── Settings.razor               # notification details setting
├── Layout/NavMenu.razor             # Request tab count
├── Program.cs                       # register the new services; start the watcher
└── wwwroot/
    ├── js/request-notify.js         # new
    └── locales/en.json, es.json     # request.*, payments.*

tests/DigiByte.Wallet.Tests/
├── ReceiveScanSetTests.cs           # + HandedOutTop cases
├── RequestAddressHandOutTests.cs    # new
├── RequestStatusCalculatorTests.cs  # new
├── RequestAmountsTests.cs           # new
├── RequestKeypadEntryTests.cs       # new
├── RequestMigrationTests.cs         # new
├── PaymentRequestServiceTests.cs    # new
├── PaymentRequestTests.cs           # + price-lock keys
├── PaymentLinkTests.cs              # + price-lock keys
└── ScanRouterTests.cs, ScanHandOffTests.cs  # fields carried

docs/PROCESS_FLOWS.md                # "Request a payment" flow
```

**Structure Decision**: The existing two-layer layout. Every rule that can be unit-tested lives in `DigiByte.Wallet`; `DigiByte.Web` holds timers, browser APIs, pages and wording.

## Delivery order (matches the spec's priorities)

1. **P1 – addresses and status**: `HandedOutTop`, `HandOutRequestAddressAsync`, `RequestStatusCalculator`, per-wallet storage and migration, `RequestWatcher` (status only), the list and detail with status.
2. **P2 – the paid moment**: watcher events, overlay, toast tap action, system notifications and their setting, lock-screen notice, the "while away" summary, the tab count.
3. **P3 – own currency**: `PriceQuoteService`, `RequestAmounts`, expiry, renew, link fields through Send and Pay.
4. **P4 – keypad**: `RequestKeypadEntry`, `RequestKeypad`, remembered currency, "Add a note".
5. **P5 – tidy list**: filters, cancel, remove.

Each step ships on its own and leaves the tab working.

## Complexity Tracking

None.
