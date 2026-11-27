# Contract: RequestWatcher and Request Hand-Out

The interfaces between the wallet library, the app-wide watcher and the pages. Names are as planned. Signatures are shown in outline only.

## WalletService (DigiByte.Wallet)

```csharp
// Hands out a new receive address for a request without changing Receive's address (FR-001, FR-003).
// Same gate, discovery retry and failures as GenerateNewReceivingAddressAsync:
//   ReceiveAddressException(TooManyUnusedPastLastUsed | TooManyUnusedPastLastPaid)  → FR-005 refusal
//   ReceiveAddressException(SingleAddress)                                           → untracked request
//   ReceiveAddressException(SaveFailed)                                              → nothing changed
Task<(string Address, int Index)> HandOutRequestAddressAsync();

// Addresses BuildHotHints always treats as hot. RequestWatcher sets it to the open requests' addresses.
// WalletService never references PaymentRequestService (which calls HandOutRequestAddressAsync).
void SetExtraHotAddresses(IReadOnlyCollection<string> addresses);

// Raised after a refresh that finds new or changed transactions, with the wallet addresses they touch.
event Action<IReadOnlyCollection<string>>? TransactionsChanged;
```

The FR-005 message takes the unused-address count from the refusal and the number of open requests from `PaymentRequestService`.

## PaymentRequestService (DigiByte.Wallet)

```csharp
Task<IReadOnlyList<PaymentRequest>> GetAllAsync(string walletId);           // runs the migration once per wallet
Task<PaymentRequest> CreateAsync(string walletId, NewRequest input);        // hands out the address, writes the label (R9)
Task<PaymentRequest> RenewAsync(string walletId, string requestId, PriceQuote quote);  // FR-018
Task CancelAsync(string walletId, string requestId);                         // FR-025
Task RemoveAsync(string walletId, string requestId);                         // FR-026: list only; the address label and watching stay
Task MarkOpenedAsync(string walletId, string requestId);                     // clears the tab count
Task ApplyAsync(string walletId, string requestId, RequestCheck result);     // stores the computed status and payments, PaidAnnouncedAt and LockNotifiedAt (never clears either)
```

`NewRequest` = an amount in DGB, or a currency amount with a `PriceQuote`, plus an optional label and message.

## RequestStatusCalculator (DigiByte.Wallet, pure)

```csharp
RequestCheck Compute(PaymentRequest request, IReadOnlyList<TransactionInfo> txsToAddress, DateTime nowUtc);
// RequestCheck: Status, Payments (with FirstSeenAt kept by txid), ArrivingSatoshis, BecamePaid, BecameArriving
```

## RequestWatcher (DigiByte.Web, app-wide)

```csharp
event Action<PaymentRequest>? PaymentArriving;   // shown only on the request's own screen
event Action<PaymentRequest>? RequestPaid;       // once per request (PaidAnnouncedAt)
event Action<int>? PaidWhileAway;                // the FR-014a summary, after the first check following start, unlock or wallet switch
event Action? Changed;                           // the list, the tab count and the lock screen re-render

void Focus(string? requestId);                   // a page shows this request: check it now, then every 3 s
Task CheckNowAsync();                            // the person pulls to refresh
IReadOnlyList<PaymentRequest> PaidUnopened { get; }   // Paid or Overpaid and not opened since, announced or not: the Request tab count, Unlock's notice
```

Rules:
- It never calls a wallet method that needs the wallet unlocked. It uses only stored addresses and `IBlockchainService.GetAddressTransactionsAsync`.
- It starts at app load for the last active wallet, before any unlock.
- Pace: 3 s for the request in focus, 30 s for live requests while visible, 60 s while hidden, with `RefreshPacer` backoff on errors. It also checks any request whose address is named by `WalletService.TransactionsChanged`.
- If a check fails, the last stored status stays and `LastCheckedAt` does not move. The UI shows "not up to date" after 2 minutes (SC-007).
- How it is shown is decided by the pure `RequestAnnouncementRule.Decide` (DigiByte.Wallet); just before announcing, the request is re-read from storage and skipped if `PaidAnnouncedAt` is set:
  - The request's own screen is visible and unlocked → `RequestPaidOverlay` with haptic.
  - Another wallet screen is visible and unlocked → a toast with a tap action.
  - The wallet is hidden → a system notification, if permission was given.
  - The wallet is locked and visible → a notice card on the lock screen.
  - The wallet is locked and hidden → the same system notification as when hidden (FR-014b), sent once (`LockNotifiedAt`), plus the lock-screen notice for when the person returns.
  - Neither locked case counts as the announcement: `PaidAnnouncedAt` stays null, so the summary follows an unlock.
  - The first check after an app start, an unlock or a wallet switch, while unlocked → one "N requests paid since you were last here" summary, which sets `PaidAnnouncedAt` for every request it counts.
  - Precedence: locked first, then the summary, then the overlay, toast or system notification.
  - Each request is announced, meaning `PaidAnnouncedAt` is set, in exactly one of the unlocked ways.

## Request notifications (wwwroot/js/request-notify.js)

```js
requestNotify.permission()        // "granted" | "denied" | "default" | "unsupported"
requestNotify.ask()               // called once, after the first request is made
requestNotify.show(title, body)   // service worker showNotification if available, else new Notification
```

- The body is "A request was paid", or "{label} · {amount} DGB" when `dgb-request-notify-details` is on.
- Never the address.
