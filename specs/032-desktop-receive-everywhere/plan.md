# Implementation Plan: Desktop Receive Everywhere

**Branch**: `feature/desktop-receive-everywhere` | **Spec**: [spec.md](spec.md) | **Builds on**: spec 030 (`feature/desktop-paste-scan`)

## Summary

Two independent pieces:

1. **Menu payments in memory.** `ScanRouter` gains `TryPayment`, which returns the address and amount of any code it routes as a payment (bare address, `digibyte:` request, this wallet's payment link) and nothing else. `PaymentHandoff` learns which Send a payment is for (personal, or a shared wallet id). `NavMenu.OnScannedAsync` hands every payment over and opens `/send?link=n` or `/multisig/{id}/send?link=n`. `MultisigSend` takes the hand-over the way `Send` already does, and its **Pay from my wallet** fallback hands the payment over too.
2. **Wide-screen order.** DigiDollar Receive and the shared-wallet Receive sheet move to spec 030's `.receive-main` markup (details first, QR in `.receive-main-qr` with the caption). Home's empty state adds the `.receive-qr-caption` under its QR.

## Technical Context

- Blazor WebAssembly (.NET), xUnit tests in `tests/DigiByte.Wallet.Tests` (they already cover `ScanRouter` and `PaymentHandoff` from the Web project).
- No CSP change, no new storage, no new network calls.
- No new locale keys: the caption reuses `receive.qr_for_phone` (en and es exist).

## Constitution Check

- **Test-first**: `ScanRouter.TryPayment`, `ScanRouter.SendPagePath` and the wallet-aware `PaymentHandoff` get failing tests before code.
- **Privacy**: payments read in the menu no longer reach the address bar (narrower than today).
- **CSP**: unchanged.
- **English/Spanish parity**: no new keys; parity tests run.

## Design

### `ScanRouter`

- `TryPayment(string? scanned, string? appBaseUri, out PaymentLinkData payment)`: true only when `Route(scanned, appBaseUri).Kind == Payment`. The payment is built by the same private helper `Route` uses for its Send URL, so the two cannot disagree. Label and message are dropped (`PaymentLinkData(address, amount, null, null)`); an amount that is not more than 0 is dropped.
- `SendPagePath(string? sharedWalletId, int counter)`: `/send?link={counter}` or `/multisig/{escaped id}/send?link={counter}`. The counter only makes the address new when that Send is already open.

### `PaymentHandoff`

- `Set(PaymentLinkData link, string? sharedWalletId = null)`.
- `Take(string? sharedWalletId = null)`: returns the waiting payment when it was set for the same Send (null = personal; ids compared ordinally), and clears what was waiting in every case.

### Pages

- `NavMenu.OnScannedAsync`: `TryPayment` first → `PayHandoff.Set(payment, sharedId)` → navigate to `SendPagePath`. The old `TryOwnPaymentLink` branch folds into this.
- `MultisigSend`: in `OnParametersSetAsync` take the hand-over for `WalletId` into `handedPayment`, before and independently of loading; `ApplyQueryPrefill` uses it first (the query second, for old links). `PayFromPersonalWallet` hands the same payment to the personal Send.
- `Send`: unchanged (`Take()` = personal).

### Layout

- `DigiDollarReceive.razor`: card becomes `receive-main`; network line, address button, Copy/Share, Download/New and hint go in `receive-main-details`; the QR in `receive-main-qr` with `receive.qr_for_phone`. The network line stays first at every width through a new `.receive-main-top` rule (order -2), so the phone layout is unchanged.
- `Multisig/ReceiveSheet.razor`: title and intro stay on top; address, buttons and Download in a details block; `QrImage` wrapped in `receive-main-qr` with the caption. `QrImage` renders an `<img>` inside `.qr-frame`, so the 9rem rule applies.
- `ReceiveEmptyState.razor`: QR column becomes a flex column with the caption below.

## Verification

- `dotnet test tests/DigiByte.Wallet.Tests` (route, hand-over, locale parity).
- Browser at 1280px and 390px: DigiDollar Receive (testnet with DigiDollar on), shared-wallet Receive, Home's empty state; menu paste of an address and a `digibyte:` request with personal and shared wallets, checking the address bar.
