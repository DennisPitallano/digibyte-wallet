# Quickstart: Validating Requests That Know When They're Paid

**Spec**: [spec.md](spec.md) | **Contracts**: [payment-link-fields.md](contracts/payment-link-fields.md), [request-watcher.md](contracts/request-watcher.md) | **Data model**: [data-model.md](data-model.md)

## Prerequisites

- .NET 10 SDK.
- The wallet running in the browser preview (`.claude/launch.json`, the web app configuration) on **testnet**.
- Two testnet wallets: wallet A (requester), an HD seed wallet with no requests yet, and wallet B (payer), with some tDGB.
- A second browser profile or device for wallet B.

## Automated checks

```powershell
dotnet build DigiByte.slnx
```

```powershell
dotnet test tests/DigiByte.Wallet.Tests
```

These must pass, including the new tests for:
- `ReceiveScanSet` with `HandedOutTop`.
- `HandOutRequestAddressAsync`: limit, undo on a failed save, and Receive's address unchanged.
- `RequestStatusCalculator`: every row of the status rules, a dropped transaction, late payments, a clock change.
- `RequestAnnouncementRule`: every outcome, the precedence, and "locked first, then unlock gives the summary".
- `WalletService.TransactionsChanged` and `SetExtraHotAddresses`.
- `PaymentLink` / `PaymentRequest` price-lock fields.
- `RequestKeypadEntry`.
- `RequestAmounts` rounding.
- Request migration.
- The locale parity and in-scope file tests.

## Manual scenarios (browser preview)

| # | Steps | Expected | Covers |
|---|---|---|---|
| 1 | In wallet A, note Receive's address. Open Request and make two 1 tDGB requests. | Each request has its own address. Receive still shows the same address as before. | FR-001, FR-003 |
| 2 | From B, pay request 1 in full. Keep request 1 open in A. | "Payment arriving", typically within 5 s of the payment appearing on the explorer, with no vibration. For SC-002, time 10 payments and check the median is under 5 s. At the first confirmation, the full-screen Paid card appears; on a phone the device vibrates. Tap Done: the request shows Paid. | US1, US2, FR-010, FR-011, SC-002 |
| 3 | Pay request 2 with 0.6 tDGB, then 0.4 tDGB, keeping A on Home. | After the first payment: "Partly paid". While the second is unconfirmed: "Partly paid · +0.4 tDGB arriving". After it confirms: a toast "… paid". Tapping it opens the request. | FR-004, FR-012 |
| 4 | Make a request, overpay it by 0.5. | "Overpaid", with the extra shown. | FR-004 |
| 5 | Set the keypad to your currency, enter 1.00, and make the request. | Both amounts, the price and a 15-minute countdown are shown. Wait 15 minutes without paying: "Expired" with "Renew at today's price"; its QR code and links are hidden. | US3, FR-017, FR-018 |
| 6 | Repeat 5, but pay half before the expiry. | "Partly paid · expired". Renew asks only for the missing part, at the current price. | FR-018 |
| 7 | Open the link from 5 in B (scan or paste), before and after the expiry. | Send shows "≈ 1.00 {cur} · Price held until …". After the expiry it shows the warning, and sending is still allowed. | FR-027a |
| 8 | In A, make a request, let the wallet auto-lock (set the lock time to 1 minute), then pay it from B. | The lock screen shows "Request paid" with no amount. After unlocking, one "1 request paid since you were last here" message appears and the request is Paid, with no full-screen card. | FR-014b |
| 9 | Allow notifications. Make a request, switch to another tab, and pay it. | A system notification: "A request was paid". Turn on "Show amount and label in notifications" and repeat: the label and amount appear, never the address. | FR-013 |
| 9b | In Settings turn on "lock when hidden". Allow notifications, make a request, switch to another tab, and pay it from B. Wait two more minutes before returning. | Exactly one system notification, "A request was paid". On return, the lock screen shows "Request paid"; after unlocking, one "1 request paid since you were last here" message. | FR-013, FR-014a, FR-014b |
| 10 | Close A, pay two open requests from B, then reopen A. | One message, "2 requests paid since you were last here". The Request tab shows 2, then the count drops as each request is opened. | FR-014a |
| 11 | Make requests until the limit. | The 21st unpaid request is refused, with the count and a link to the Open list. The message does not suggest that cancelling frees space. | FR-005 |
| 12 | Cancel a request, then pay it. Remove another paid request. | The cancelled one shows the late payment with a note. The removed one disappears from the list, and its payment keeps its label in Activity. The balance is correct in both cases. | FR-020, FR-025, FR-026, SC-005 |
| 13 | Block the explorer in devtools (offline). | Statuses keep their last value, marked "not up to date" after 2 minutes. DGB requests can still be made. The currency option says why it isn't available. | SC-007, FR-021 |
| 14 | Load a device that has requests from before the update. | They show "Not tracked", and their QR code and links still work. | FR-006 |
| 15 | Switch the app language to Spanish. | Every new screen and message is translated. | R10 |
| 16 | In devtools, watch network traffic during 1 to 12. | No request to a server operated by this project contains a label, message, amount or status. Only the explorer and price lookups the wallet already makes. | SC-006 |
| 17 | Time Home → Request → type 25 → Request. | QR code shown in under 10 s and no more than 4 taps. | SC-003 |
| 18 | Open a shared (multisig) wallet. | The Pending tab is unchanged. | FR-032 |
