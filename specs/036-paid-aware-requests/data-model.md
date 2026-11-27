# Data Model: Requests That Know When They're Paid

**Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

All of this is stored on the device only, in the existing non-secret `ISecureStorage` (IndexedDB) or in localStorage. Nothing here is a secret, and nothing is sent to a server (FR-028).

## PaymentRequest (changed)

`src/DigiByte.Wallet/Models/PaymentRequest.cs`. Stored as a JSON list under `payment_requests:{walletId}` (was `payment_requests`, shared by all wallets; see Migration).

| Field | Type | Notes |
|---|---|---|
| `Id` | string | Unchanged. |
| `Address` | string | Unchanged. For tracked requests, the request's own address (FR-001). |
| `AddressIndex` | int? | **New.** The receive index the address came from. Null for untracked requests. |
| `Tracked` | bool | **New.** False for legacy requests and single-key wallets, which show "Not tracked" (FR-006). Defaults to false when the field is missing in stored JSON. |
| `AmountDgb` | decimal? | Unchanged. For a currency request, the fixed DGB amount (FR-017). |
| `FiatAmount` | decimal? | **New.** The amount the person typed in their currency. |
| `FiatCurrency` | string? | **New.** ISO code from `FiatCurrencies`. |
| `FiatPrice` | decimal? | **New.** Price per DGB used for the conversion. |
| `Label`, `Message` | string? | Unchanged. The message is now shown on the detail view (FR-007). |
| `CreatedAt` | DateTime (UTC) | Unchanged. |
| `ExpiresAt` | DateTime? (UTC) | Existing field, now used: `CreatedAt + 15 min` for currency requests, null for DGB requests (FR-017, FR-019). |
| `CancelledAt` | DateTime? (UTC) | **New.** Set when the person cancels. |
| `RenewedFromId` | string? | **New.** Set on a request made by "Renew at today's price" (FR-018). |
| `Payments` | list of `RequestPayment` | **New.** Last computed payments, kept for display while offline. |
| `Status` | `PaymentRequestStatus` | Existing field, now the last computed status. It is always recomputed by `RequestStatusCalculator` and never edited by hand. |
| `PaidAnnouncedAt` | DateTime? | **New.** When the Paid moment was shown, in any form (overlay, toast, notification, summary). It is set once, which guarantees "once per request" (FR-010). The lock-screen notice and the locked notification do not set it, so the summary still follows an unlock. |
| `LockNotifiedAt` | DateTime? | **New.** When the system notification was sent for this request while the wallet was locked and hidden (FR-014b). Checked so that notification is sent at most once. |
| `OpenedSincePaid` | bool | **New.** False once paid until the person opens the request. Drives the Request tab count (FR-014a). |
| `LastCheckedAt` | DateTime? | **New.** When its address was last checked. Older than 2 minutes while the wallet is online shows "not up to date". |

### PaymentRequestStatus (enum, changed)

`Waiting`, `PaymentArriving`, `PartlyPaid`, `Paid`, `Overpaid`, `Expired`, `PartlyPaidExpired`, `Cancelled`, `NotTracked`.

The existing values `Pending` and `Expired` are kept for JSON reading only: `Pending` reads as `Waiting`.

## RequestPayment (new)

| Field | Type | Notes |
|---|---|---|
| `TxId` | string | |
| `AmountSatoshis` | long | The sum of this transaction's outputs to the request's address. |
| `Confirmed` | bool | Confirmations ≥ 1. |
| `FirstSeenAt` | DateTime (UTC) | First time this device saw the transaction. Kept across recomputes by transaction id. |
| `Late` | bool | `FirstSeenAt > ExpiresAt` or `FirstSeenAt > CancelledAt` (FR-020). |

## Status rules (RequestStatusCalculator, pure)

Inputs: the request, the current transactions paying its address, and `now`. Recomputed from scratch on each check. A transaction missing from the list no longer counts (FR-009).

```
confirmed = sum(confirmed payments)    pending = sum(unconfirmed payments)
asked     = AmountDgb in satoshis (null = any amount)

if !Tracked                                  → NotTracked
if CancelledAt and confirmed == 0            → Cancelled            (+ pending shown as note)
if asked is null:  confirmed > 0             → Paid
                   else pending > 0          → PaymentArriving
                   else                      → Waiting / Cancelled
else:
  confirmed > asked                          → Overpaid
  confirmed == asked                         → Paid
  0 < confirmed < asked:
      expired (now ≥ ExpiresAt)              → PartlyPaidExpired
      cancelled                              → Cancelled (amount shown)
      else                                   → PartlyPaid
  confirmed == 0:
      cancelled                              → Cancelled
      expired                                → Expired
      pending > 0                            → PaymentArriving
      else                                   → Waiting
ArrivingNote = pending, when the status is not PaymentArriving and pending > 0   (FR-004 "+… arriving")
```

- **Paid moment**: the first recompute that yields `Paid` or `Overpaid` while `PaidAnnouncedAt` is null raises `RequestPaid` and sets `PaidAnnouncedAt`.
- **Arriving notice**: the first recompute where the confirmed total plus the pending total reaches the amount asked (or pending > 0 for a request with no amount) raises `PaymentArriving`, which is shown only on screen (FR-010).
- **Filters** (FR-024): Open = Waiting, PaymentArriving, PartlyPaid. Paid = Paid, Overpaid. Expired = Expired, PartlyPaidExpired, Cancelled. NotTracked requests appear only under "All".

## WalletInfo (changed)

`src/DigiByte.Wallet/Models/WalletInfo.cs`. One new field, stored unencrypted in `wallet_info_{id}` like the other index fields.

| Field | Type | Notes |
|---|---|---|
| `HandedOutTop` | int? | **New.** The highest receive index ever handed out, to Receive or to a request. Null means it equals `NextReceivingIndex` (true for every wallet before this update). It is never lowered, and only the hand-out methods raise it. |

### ReceiveScanSet rules (amends spec 027 data-model "Moving the address on show")

- `top = HandedOutTop ?? shown`.
- `NextNewIndex = max(shown + 1, Anchor, top + 1)`.
- `BaseCount = max(20, used, floor, shownIsWatched ? 0 : shown + 1, topIsWatched ? 0 : top + 1)`.
- `CanHandOutNew = NextNewIndex < Anchor + 20` (unchanged): requests and Receive share the 20-unpaid budget (FR-005).
- **Recording a request hand-out** (`RecordHandOut(info, index)`): if `index > BaseCount`, raise the floor to `BaseCount` and add `index` as an unpaid watched entry. In every case set `HandedOutTop = index`. `NextReceivingIndex` is never assigned. Returns an undo that restores both fields and removes only the entry it added.
- `GenerateNewReceivingAddressAsync` (Receive) also raises `HandedOutTop` to the index it shows.
- The writers of `NextReceivingIndex` are still exactly the two from spec 027. `HandedOutTop` is a third field with its own two writers.

## Other stored values

| Key | Where | Value |
|---|---|---|
| `address-labels:{walletId}` | ISecureStorage (existing) | The request's label is written for its address on creation (FR-008). |
| `dgb-request-currency` | localStorage | `"DGB"` or `"FIAT"`: the keypad currency last used (FR-016a). |
| `dgb-request-notify-details` | localStorage | `"true"` shows the amount and label in notifications and on the lock screen. Default off (FR-013). |
| `dgb-request-notify-asked` | localStorage | Set once notification permission has been asked, so it is never asked twice. |

## Migration (first load of a wallet after the update)

1. Read the legacy `payment_requests` list, if any.
2. Move every legacy request whose `Address` is one of this wallet's known receive addresses to `payment_requests:{walletId}`, with `Tracked = false` and `Status = NotTracked`.
3. Write the rest back to `payment_requests`, or remove the key when nothing is left.
4. Older JSON without the new fields reads with safe defaults (`Tracked = false`, empty `Payments`).

## State transitions

```
            ┌──────────── cancel ──────────────┐
            ▼                                  │
Waiting ─ seen ─▶ PaymentArriving ─ 1 conf ─▶ Paid / Overpaid       (terminal for display;
   │                    │ dropped                ▲                   late extra money → Overpaid)
   │                    ▼                        │
   │               (back to earlier)      PartlyPaid ── rest confirms
   │                                          │
   ├─ 15 min (currency only) ─▶ Expired       ├─ 15 min ─▶ PartlyPaidExpired ── late rest ─▶ Paid
   └─ cancel ─▶ Cancelled ◀───────────────────┘ cancel
```
