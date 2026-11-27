# Contract: sessions, checkout and webhooks

Spec 033's contracts (`specs/033-digipay-dd-everywhere/contracts/`) still apply, except where this file changes them.

## C1. Creating DD payments and options

- `POST /v1/pay/sessions` with `currency: "DD"`, POS and link sales, and `POST /v1/pay/sessions/{id}/options/dd` all allocate the address from the store key's sequence (`DdAddressAllocator`). There's no outside call.
- **Removed**: 503 `digidollar_node_unavailable`. A DD payment or option is created even while the explorer is down (FR-010).
- **Unchanged**: 409 `digidollar_not_enabled_for_store`, `dd_option_not_allowed`, and `dd_option_unavailable` with reasons `dd_disabled`, `no_rate` and `out_of_range`.
- **New**: the `dd_option_unavailable` reason `dd_service_unavailable`, when the cached explorer health (R6) says DD is unavailable at the moment the buyer switches. This matches spec US4 scenario 1, where checkout shows DD as unavailable. Creating the option still makes no outside call. The check reads only the cached health, so FR-010 holds.

## C2. Public session (`GET /v1/pay/sessions/{id}`)

- `ddAvailable` / `ddReason` keep their shape.
- The reason `node_unavailable` becomes `dd_service_unavailable`. It comes from explorer health for the session's network (R6).
- **Removed**: the owner-only payout info (`payout`, `reversalAbsorbed`) from `SessionPayoutInfo`.

## C3. Payment states

They're unchanged: pending, seen, paid, confirmed, underpaid, expired, and the 3-day late window. DD amounts come from explorer history (R5). The meaning of each field:

| Field | Meaning |
|---|---|
| `receivedCentsDd` | Total DD received by the address (outputs), not its current balance |
| `paidTxid` | The first transaction paying the address |
| `confirmations` | Real confirmations of the transaction that completed the amount due. Confirmed at 6. |

## C4. Webhooks

- **Kept**:
  - `session.seen`
  - `session.paid`
  - `session.confirmed`
  - `session.underpaid`
  - `session.expired`
  - `session.extra_payment` (033, which tells the merchant to refund from their own wallet)
- **Removed** (034):
  - `payout.queued`
  - `payout.sent`
  - `payout.confirmed`
  - `payout.failed`
  - `payout_address.change_requested`
  - `payout_address.changed`
  - any `operator.*` events
- **Samples**: the express, flask and .NET handlers stop listing payout events. Session-less acking stays, since it's harmless.

## C5. Checkout, POS, tip page and embed

The behaviour is unchanged from 033, but the copy changes:

- **DD shown as unavailable**: says "DigiDollar is temporarily unavailable" (not "node").
- **Merchant text**: says "DigiDollar goes straight to your wallet".
