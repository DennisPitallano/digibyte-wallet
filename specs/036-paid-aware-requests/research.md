# Research: Requests That Know When They're Paid

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-28

Each section records a decision, why it was made, and what else was considered. File references are to the code as of `main` at `94f368c`.

## R1. Handing out a request's own address without moving Receive's address

**Finding.** `WalletService.GenerateNewReceivingAddressAsync` (`src/DigiByte.Wallet/Services/WalletService.cs:1118-1163`) is the only method that hands out a new receive address. It always makes the new index the address on show (`MoveAddressOnShow`, WS:1187-1214). `ReceiveScanSet.NextNewIndex = max(shown + 1, Anchor)` (`ReceiveScanSet.cs:38`), so if a request index were recorded without moving `shown`, the next hand-out would return the same index. Spec 027's data model (`specs/027-deep-receive-search/data-model.md:59-68`) allows only two writers of `NextReceivingIndex`, and an index below `BaseCount` can never be a watched entry (`Normalize` drops it).

**Decision.**
- Add one persisted field, `WalletInfo.HandedOutTop` (`int?`): the highest receive index ever handed out, whether to Receive or to a request.
- `ReceiveScanSet` reads it:
  - `NextNewIndex = max(shown + 1, Anchor, HandedOutTop + 1)`, so neither Receive nor a request ever gets an index already given out.
  - `BaseCount` also counts `HandedOutTop + 1` when that index was contiguous with the base run, the same way it counts `shown + 1` today.
  - `CanHandOutNew` is unchanged (`NextNewIndex < Anchor + 20`), so requests and Receive share one budget of 20 unpaid addresses. That is exactly FR-005.
- Add `WalletService.HandOutRequestAddressAsync()`, a sibling of `GenerateNewReceivingAddressAsync`. It uses the same `AddressOnShowGate`, the same discovery retry, the same `TooManyUnused*` failures and the same persist-or-undo.
  - It calls a new `RecordHandOut(info, index)`. That follows `MoveAddressOnShow`'s rules (watched unpaid entry when not contiguous, floor raised) but never assigns `NextReceivingIndex`.
  - It returns the SegWit address and its index.
- `GenerateNewReceivingAddressAsync` also raises `HandedOutTop`, so Receive's own hand-outs keep the field right.
- The Digi-ID sweep's room calculation reads `NextNewIndex` and so stays correct.
- Spec 027's rule "no other assignment of `NextReceivingIndex`" still holds. The data model records `HandedOutTop` as a third, separate field.

**Rationale.** One field keeps every existing rule intact: the base run only grows, watched entries come only from hand-outs and searches, and the gap limit is unchanged. A restore from seed still finds every request payment, because request indexes stay within 20 of the last paid one.

**Alternatives considered.**
- Derive "highest handed out" from the watched list. Rejected: contiguous hand-outs live in the base run, not the watched list.
- A separate address branch or account for requests. Rejected: a restore from seed would not scan it, so payments could be invisible.
- Moving the address on show for requests too. Rejected: it breaks FR-003.

## R2. Where the request status is worked out, and how payments are found

**Finding.**
- `IBlockchainService.GetAddressTransactionsAsync(address)` (`IBlockchainService.cs:9-51`) returns each transaction with `Confirmations` and `Outputs` (address, amount), so for one address it gives every payment, confirmed or pending.
- The wallet's balance sync already checks every index in `ReceiveScanSet.Indexes`, including watched ones, so the balance never depends on the request feature (SC-005).
- `HotAddressSelector` (spec 024) picks which addresses are checked often. Its hints come from `WalletService.BuildHotHints` (WS:841-863).

**Decision.**
- A pure `RequestStatusCalculator` in `DigiByte.Wallet` turns one request plus the current list of transactions paying its address into its status, its received payments and its "+… arriving" amount. It works from the request, that transaction list and the current time. It recomputes everything on each check. A transaction that drops out of the list stops counting (FR-009). Nothing is accumulated, so nothing can drift.
- Late flags (FR-020) compare the first time this device saw a payment with `ExpiresAt` or `CancelledAt`. That first-seen time is stored per transaction id on the request, because the device has no other record of it.
- Open-request addresses are added to the hot hints through `WalletService.SetExtraHotAddresses`, which `RequestWatcher` calls, so the normal refresh treats them as hot. This covers SC-005 and the history view; the watcher below covers live status.

**Alternatives considered.**
- Reading status out of `TransactionRecord` from the history sync. Rejected: `AmountSatoshis` is the net for the whole wallet, not per address, and the sync does not run while locked or on the Payments page.

## R3. Who watches, and what happens while locked or hidden

**Finding.**
- `AutoRefreshLoop` is created per page (Home, Receive) and skips ticks while the tab is hidden.
- Auto-lock (`AppState.LockNow` / `OnTabHidden` / the timer) only sets `IsWalletUnlocked = false`. `MainLayout` then navigates to `/unlock`, which disposes the page.
- `WalletService.LockWalletAsync` is never called by the web app, so the wallet session (including keys) stays in memory after auto-lock. See the pre-existing note below.
- Requests are in plain `ISecureStorage` (IndexedDB), readable while locked, and `PaymentRequest.Address` already stores the address string.

**Decision.**
- A new app-wide `RequestWatcher` in `DigiByte.Web`, registered like other app-wide services (scoped, which is a singleton in WASM).
- It watches by stored address string only, through `IBlockchainService`. It never calls an `EnsureUnlocked` method, so it works while locked (FR-014b), and it would keep working if auto-lock is later changed to clear the session.
- What it checks (a "live" request is one that is Waiting, Payment arriving or Partly paid, or one that expired or was cancelled in the last 24 hours):
  - The request open on screen every 3 s, paced by `RefreshPacer`'s backoff on errors.
  - Other live requests every 30 s while the wallet is visible, and every 60 s while it is hidden. Browsers throttle hidden timers to about once a minute anyway.
  - All other requests are re-checked when `WalletService.TransactionsChanged` (a new event raised after a refresh that finds new or changed transactions) names their address, and whenever the person opens them. This is how late payments to old requests are found (FR-020) without polling every request forever: they are noticed at the next balance refresh (Home or Receive open), or as soon as that request is opened.
- It keeps working while the tab is hidden, unlike `AutoRefreshLoop`. That is required for FR-013.
- The watcher starts at app load for the last active wallet, before any unlock, so FR-014b also covers an app opened while locked. It tells `WalletService` which addresses are hot through `SetExtraHotAddresses`, so `WalletService` never references `PaymentRequestService` (which itself calls `WalletService` to hand out addresses).
- The watcher covers the active wallet's requests only. On a wallet switch it runs one check of the new wallet's live requests, and any it finds paid go into the FR-014a summary.

**Pre-existing note (outside this feature).** Auto-lock leaves the decrypted wallet session in memory until the page is closed. This feature does not rely on that and does not change it. It is worth its own security review.

## R4. Telling the person: overlay, in-app message, system notification, lock screen, tab count

**Finding.**
- `FirstReceiveCelebration.razor` and `TxSuccessOverlay.razor` already provide a full-screen card that the person dismisses, with `domHelpers.haptic` (`dom-helpers.js:89-92`).
- `NotificationService` toasts have no tap action.
- Nothing uses the browser `Notification` API. The service worker handles no notifications.
- The nav already has a count badge pattern (`NavMenu.razor:72-83`, `CountBadge`).

**Decision.**
- `RequestWatcher` raises `PaymentArriving` and `RequestPaid` events. `RequestPaid` fires once per request; `PaidAnnouncedAt` is persisted so a reload never repeats it, and it is re-read from storage just before announcing so a second tab does not repeat it either.
- Which way a paid request is announced is decided by a pure `RequestAnnouncementRule` in `DigiByte.Wallet`, so the rule is unit-tested; the web services only act on its answer.
- New `RequestPaidOverlay` component, built on the `celebrate-card` pattern, shown when the request it belongs to is on screen (FR-010, FR-011).
- `NotificationService.Show` gains an optional tap action (`Action? onTap`). FR-012 and FR-014a use it.
- New `wwwroot/js/request-notify.js`:
  - `permission()`, `ask()` and `show(title, body)` use `registration.showNotification` when a service worker is active, and `new Notification` otherwise.
  - `ask()` is called only after the first request is made (FR-013).
  - No push subscription and no server, so the CSP is unchanged (FR-014).
- The setting "Show amount and label in notifications" is stored in localStorage as `dgb-request-notify-details`, default off, and is shown in Settings.
- FR-014a: the first completed check after the app starts, an unlock or a wallet switch collects every request that became Paid or Overpaid without an announcement, and shows one sticky toast with a tap action. The Request tab badge counts requests that are Paid or Overpaid with `OpenedSincePaid == false`, whether announced or not, through a `RequestAttentionState` with a `Changed` event, like `MultisigAttentionState`.
- Lock screen: showing a paid request on the lock screen does not mark it announced, so the person still gets the summary after unlocking (FR-014a). `Unlock.razor` shows a notice card from `RequestWatcher` ("Request paid", with details only if the setting is on). It uses the existing notice-card pattern (`Unlock.razor:56-73`).
- Locked and in the background: the same system notification as when hidden is sent once per request (FR-014b), recorded in `LockNotifiedAt` and re-read from storage before sending so a second tab does not repeat it. Like the lock-screen notice, it does not mark the request announced.

## R5. The price for currency requests

**Finding.**
- `IBlockchainService.GetDgbPriceAsync(currency)` (resolved to `FallbackBlockchainService`, with a 5-minute cache) throws on failure.
- Pages each wrap it in `MemoryCacheService` with try/catch.
- Nothing returns when a price was fetched.
- The currency is `AppState.FiatCurrency`, with `FiatCurrencies` for symbols and decimals.

**Decision.**
- New `PriceQuoteService` in `DigiByte.Web` returns `PriceQuote(Price, Currency, FetchedAt)` or null, never throws, and reuses the same cache key as the pages.
- A quote older than 5 minutes is refreshed before a request is made, so a request never locks a stale price.
- The conversion is pure, in `RequestAmounts` (`DigiByte.Wallet`): DGB = currency amount ÷ price, rounded half-up to 8 decimal places. The request stores the currency, the currency amount and the price.
- The expiry is `CreatedAt + 15 min` in UTC. Paid or Overpaid always wins over expiry, so a clock change never turns a paid request into an expired one.

## R6. Carrying the price lock in the link (FR-027a)

**Finding.**
- `PaymentRequest.ToUri` / `FromUri` and `PaymentLink.TryRead` (`PaymentLink.cs:51-95`) ignore unknown keys, but `TryRead` rejects the whole link if any value fails to unescape.
- `ScanRouter` (`ScanRouter.cs:98-109`), `ScanHandOff` (`:50-51`) and `Send.TryTakeHandoff` (`Send.razor:939-953`) keep only the address and amount.
- `Pay.razor` only shows a card when the device has no wallet; otherwise it hands straight to Send.

**Decision.**
- Three new query keys, written only for currency requests: `expires` (Unix seconds, UTC), `fiat` (an invariant decimal) and `currency` (an ISO code from `FiatCurrencies`).
- BIP21 says wallets must ignore unknown keys that do not start with `req-`, so the DGB address and amount keep working everywhere. The format is in [contracts/payment-link-fields.md](contracts/payment-link-fields.md).
- Reading:
  - A bad value in any of the three keys drops all three and keeps the link (edge case "treated as having none").
  - The three ride through `PaymentLinkData`, `ScanRouter`, `ScanHandOff` and `PaymentHandoff` into Send.
  - Send shows the currency amount and "Price held until HH:mm", or after expiry a warning: "The price for this request expired at HH:mm; the amount may be out of date". Sending is never blocked.
  - `Pay.razor`'s no-wallet card shows the same line.
- Everything stays after the `#` (FR-029), so the web host never sees it.

## R7. The keypad

**Finding.**
- The wallet has no keypad.
- DigiPay's POS has one (`Pos.razor:226-262`, `Pos/PosEntry.cs`) in `DigiByte.Pay.Web`, which the wallet cannot reference.
- `AmountEntryText` holds the wallet's amount parsing.

**Decision.**
- New pure `RequestKeypadEntry` in `DigiByte.Wallet`, modelled on `PosEntry`'s press/fits/parse rules. It uses 8 decimals for DGB and `FiatCurrencies.DecimalsFor` for the person's currency.
- New `RequestKeypad.razor` component in `DigiByte.Web`.
- The remembered currency is stored in localStorage as `dgb-request-currency` (FR-016a).
- Moving `PosEntry` into a shared library was rejected for this feature, because it would change DigiPay in a wallet PR. That is a possible later clean-up.

## R8. Storage and old requests

**Finding.** Requests are stored under one key, `payment_requests`, for every wallet on the device. The spec assumed they were per wallet.

**Decision.**
- Store them per wallet as `payment_requests:{walletId}`.
- On a wallet's first load after the update, legacy requests whose address is one of that wallet's known receive addresses move to its list. Those, and any left over, are marked `Tracked = false` ("Not tracked", FR-006).
- Legacy requests that match no wallet stay under the old key, untouched. Remove that key only when it becomes empty.
- New requests in single-key wallets are created with `Tracked = false`.
- The spec's assumption is corrected to match.

## R9. The label in Activity (FR-008, FR-026)

**Finding.** `AddressLabelService` (`address-labels:{walletId}`) already labels addresses, and Activity resolves labels through `ActivityAddressIndex.Display` on `tx.WalletAddresses`.

**Decision.**
- When a request with a label is made, also call `AddressLabelService.SetLabelAsync(walletId, address, label)`. Activity, the transaction detail and CSV export then show it with no new code.
- Removing a request leaves the address label, so payments keep their label (clarification 9).
- This also means a payment that arrives after the request was removed still shows the label, because it is labelled by address. The spec's edge case is updated to say so. It is simpler and loses nothing.

## R10. Translations and docs

**Decision.**
- All new text goes in `wwwroot/locales/en.json` and `es.json` under `request.*` (already in `LocaleParityTests.FeaturePrefixes`) and `payments.*`.
- `Payments.razor`, `RequestKeypad.razor`, `RequestPaidOverlay.razor` and `Unlock.razor` are on `LocaleFilesTests.InScopeFiles`.
- `docs/PROCESS_FLOWS.md` gains a "Request a payment" flow, as the constitution requires for behaviour changes users can see.
